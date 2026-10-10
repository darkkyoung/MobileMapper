using System.Diagnostics;
using MobileMapper.Core;
using MobileMapper.Device;
using MobileMapper.Media;
using MobileMapper.Scrcpy;

namespace MobileMapper.Session;

/// <summary>One selected device and one generation of sockets. No UI calls or input replay.</summary>
public sealed class MirrorSession(AdbRuntime adb, NativeMedia media, string serverPath)
{
    private readonly object inputLock = new();
    private readonly TouchState touches = new();
    private readonly SessionStateMachine machine = new();
    private ControlWriter? writer;
    private VideoGeometry? geometry;
    private static long geometryEpoch;
    private CancellationTokenSource? runCancellation;
    public event Action? Changed;
    public DiagnosticsLog Log { get; } = new();
    public StreamCounters Counters { get; } = new();
    public SessionState State => machine.State;
    public string LastError { get; private set; } = "";
    public string SelectedDevice { get; private set; } = "";
    public VideoGeometry? Geometry { get { lock (inputLock) return geometry; } }
    public long InputGeneration { get { lock (inputLock) return touches.Generation; } }
    public int ActiveContacts { get { lock (inputLock) return touches.Count; } }
    public bool InputEnabled { get { lock (inputLock) return touches.Enabled; } }
    private void StateTo(SessionState state)
    { machine.MoveTo(state); Log.Add("session", state.ToString()); Changed?.Invoke(); }
    public void RequestStop() => runCancellation?.Cancel();
    public void Arm()
    {
        lock (inputLock)
        {
            if (State != SessionState.Streaming || writer is null || geometry is not { } g || media.Stats.Generation != (ulong)g.Generation)
                throw new InvalidOperationException("Wait for a current video frame before enabling touch.");
            touches.Arm();
        }
        Changed?.Invoke();
    }
    public bool Touch(string owner, TouchAction action, NormalizedPoint point, long expectedGeneration)
    {
        lock (inputLock)
        {
            if (!touches.Enabled || expectedGeneration != touches.Generation || writer is null || geometry is not { } g ||
                media.Stats.Generation != (ulong)g.Generation) return false;
            TouchFrame? frame = action switch
            {
                TouchAction.Down => touches.Down(owner, point, g, expectedGeneration),
                TouchAction.Move => touches.Move(owner, point, g, expectedGeneration),
                TouchAction.Up => touches.Up(owner, expectedGeneration),
                _ => throw new ArgumentOutOfRangeException(nameof(action))
            };
            if (frame is { } value)
            {
                try { writer.Enqueue(value); }
                catch { touches.ReleaseAll(); runCancellation?.Cancel(); throw; }
            }
            return frame is not null;
        }
    }
    public async Task ReleaseAsync()
    {
        Task barrier;
        lock (inputLock)
        {
            touches.ReleaseAll();
            barrier = writer?.ReleaseAllAsync(touches.Generation) ?? Task.CompletedTask;
        }
        Changed?.Invoke();
        try { await barrier.WaitAsync(TimeSpan.FromMilliseconds(650)); }
        catch (Exception e) when (e is IOException or OperationCanceledException or TimeoutException)
        { Log.Add("input", "Remote release could not be confirmed; local contacts were cleared."); }
    }
    public async Task RunAsync(string serial, string identity, CancellationToken ct)
    {
        using var run = CancellationTokenSource.CreateLinkedTokenSource(ct);
        runCancellation = run;
        try
        {
            for (int attempt = 0; ; attempt++)
            {
                run.Token.ThrowIfCancellationRequested();
                StateTo(SessionState.Connecting);
                try
                {
                    if (!adb.IsAlive) throw new IOException("The private ADB daemon stopped. Restart MobileMapper.");
                    if (attempt > 0) serial = await RediscoverAsync(identity, run.Token);
                    SelectedDevice = serial;
                    StateTo(SessionState.StartingServer);
                    await StreamOnceAsync(serial, run.Token);
                    throw new IOException("The Android video stream ended.");
                }
                catch (OperationCanceledException) when (run.IsCancellationRequested) { throw; }
                catch (Exception e) when (e is IOException or System.Net.Sockets.SocketException or InvalidOperationException or TimeoutException or OperationCanceledException)
                {
                    // Only application-authored exception messages are surfaced. Never ADB stdout/stderr.
                    LastError = e is OperationCanceledException ? "The server handshake timed out." : e.Message;
                    Log.Add("stream", "Session stopped; sockets and local contacts cleared.");
                    if (attempt >= RetryPolicy.MaxAttempts || !adb.IsAlive)
                    { StateTo(SessionState.NeedsUserAction); return; }
                    Counters.Reconnects++;
                    StateTo(SessionState.Recovering);
                    await Task.Delay(RetryPolicy.Delay(attempt), run.Token);
                }
            }
        }
        catch (OperationCanceledException) when (run.IsCancellationRequested)
        { StateTo(SessionState.Stopping); StateTo(SessionState.Idle); }
        finally { runCancellation = null; await ReleaseAsync(); }
    }
    private async Task<string> RediscoverAsync(string identity, CancellationToken ct)
    {
        var services = await adb.Client.DiscoverAsync(ct);
        foreach (var service in services.Where(s => !s.IsPairing))
            try { await adb.Client.ConnectAsync(service.Endpoint, ct); } catch (IOException) { }
        foreach (var candidate in await adb.Client.DevicesAsync(ct))
            if (candidate.State == "device" && await adb.Client.IdentityAsync(candidate.Serial, ct) == identity)
                return candidate.Serial;
        throw new IOException("The selected phone was not rediscovered. Check Wireless Debugging; refresh or enter its current connection endpoint.");
    }
    private async Task StreamOnceAsync(string serial, CancellationToken ct)
    {
        await using var connection = await ScrcpyConnection.OpenAsync(adb, serial, serverPath, ct);
        using var lifetime = new CancellationTokenSource();
        using var stopWait = CancellationTokenSource.CreateLinkedTokenSource(ct, lifetime.Token);
        using var packets = new PacketQueue();
        using var controlWriter = new ControlWriter(connection.Control);
        lock (inputLock) { writer = controlWriter; geometry = null; }
        var writeTask = controlWriter.RunAsync(lifetime.Token);
        await ReleaseAsync();
        StateTo(SessionState.Streaming);
        var tasks = new[] {
            Task.Run(async () => {
                while (true) {
                    var record = await connection.Protocol.ReadAsync(lifetime.Token);
                    packets.Write(record); if (record is EncodedPacket p && !p.IsConfig) Interlocked.Increment(ref Counters.Received);
                    Interlocked.Exchange(ref Counters.QueuedBytes, packets.Bytes);
                    Interlocked.Exchange(ref Counters.QueueDepth, packets.Count);
                }
            }),
            Task.Run(async () => {
                long currentEpoch = 0;
                while (true) {
                    var record = await packets.ReadAsync(lifetime.Token);
                    if (record is GeometryRecord changed) {
                        bool hadContacts; lock (inputLock) hadContacts = touches.Count != 0;
                        await ReleaseAsync();
                        if (hadContacts) throw new IOException("Video orientation changed during a touch; restarting safely.");
                        currentEpoch = Interlocked.Increment(ref geometryEpoch);
                        media.Reset(currentEpoch);
                        lock (inputLock) geometry = changed.Geometry with { Generation = currentEpoch };
                        Changed?.Invoke();
                    } else if (record is EncodedPacket packet) {
                        using (packet) {
                            if (Stopwatch.GetElapsedTime(packet.ReceivedAt) > TimeSpan.FromMilliseconds(250))
                                throw new IOException("Compressed video is more than 250 ms behind; restarting to discard stale video.");
                            media.Decode(packet.Data, packet.Pts, packet.IsConfig);
                        }
                    }
                }
            }),
            writeTask,
            TouchProtocol.DrainDeviceMessagesAsync(connection.Control, lifetime.Token),
            Task.Run(async () => {
                while (true) {
                    await Task.Delay(200, lifetime.Token);
                    if (connection.ServerExited || !adb.IsAlive) throw new IOException("The Android or ADB server stopped.");
                    if (media.Stats.Error != 0) throw new InvalidOperationException("The native presenter failed. Restart MobileMapper and check the graphics driver.");
                }
            }),
            Task.Delay(Timeout.Infinite, stopWait.Token)
        };
        try { await await Task.WhenAny(tasks); }
        finally
        {
            await ReleaseAsync();
            lock (inputLock) { writer = null; geometry = null; }
            lifetime.Cancel();
            // The cancellation task belongs to the parent; do not await it on a spontaneous stream failure.
            try { await Task.WhenAll(tasks.Take(tasks.Length - 1)); } catch (Exception) { }
            Interlocked.Exchange(ref Counters.QueueDepth, 0); Interlocked.Exchange(ref Counters.QueuedBytes, 0);
        }
    }
}
