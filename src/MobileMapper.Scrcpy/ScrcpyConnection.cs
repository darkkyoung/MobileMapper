using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using MobileMapper.Device;

namespace MobileMapper.Scrcpy;

public sealed class ScrcpyConnection : IAsyncDisposable
{
    private readonly AdbRuntime adb;
    private readonly string serial;
    private readonly string remote;
    private readonly CancellationTokenSource drains = new();
    private Process? server;
    private Task<string>[] output = [];
    private int forwardPort;
    private TcpClient? video, control;
    public Stream Video => video!.GetStream();
    public Stream Control => control!.GetStream();
    public bool ServerExited => server?.HasExited != false;
    public VideoProtocol Protocol { get; private set; } = null!;
    private ScrcpyConnection(AdbRuntime adb, string serial, string scid)
    { this.adb = adb; this.serial = serial; remote = $"/data/local/tmp/mobilemapper-{scid}.jar"; }

    public static async Task<ScrcpyConnection> OpenAsync(AdbRuntime adb, string serial, string serverPath, CancellationToken ct)
    {
        if (!adb.IsAlive) throw new IOException("The private ADB daemon stopped. Restart MobileMapper.");
        if (!AdbParsing.IsSelector(serial)) throw new ArgumentException("Invalid device selector.");
        await ServerArtifact.VerifyAsync(serverPath, ct);
        var scid = RandomNumberGenerator.GetInt32(1, int.MaxValue).ToString("x8", CultureInfo.InvariantCulture);
        var session = new ScrcpyConnection(adb, serial, scid);
        try
        {
            var state = await adb.Client.SelectedAsync(serial, ["get-state"], ct);
            AdbClient.Check(state);
            if (state.Output.Trim() != "device") throw new IOException("Select an authorized, online device.");
            AdbClient.Check(await adb.Client.SelectedAsync(serial, ["push", serverPath, session.remote], ct, TimeSpan.FromSeconds(30)));
            var forward = await adb.Client.SelectedAsync(serial, ["forward", "tcp:0", $"localabstract:scrcpy_{scid}"], ct);
            AdbClient.Check(forward);
            if (!int.TryParse(forward.Output.Trim(), out session.forwardPort) || session.forwardPort is < 1 or > 65535)
                throw new IOException("ADB did not return the owned forward port.");
            // All shell-side tokens below are constants or locally-generated hex; no user text is interpolated.
            session.server = adb.Runner.StartServerShell(["-s", serial, "shell", $"CLASSPATH={session.remote}",
                "app_process", "/", "com.genymobile.scrcpy.Server", ServerArtifact.Version,
                $"scid={scid}", "tunnel_forward=true", "audio=false", "control=true", "video_codec=h264",
                "max_size=1280", "max_fps=60", "video_bit_rate=8000000", "clipboard_autosync=false", "cleanup=true"]);
            session.output = [AdbProcess.DrainAsync(session.server.StandardOutput, session.drains.Token),
                AdbProcess.DrainAsync(session.server.StandardError, session.drains.Token)];
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
            deadline.CancelAfter(TimeSpan.FromSeconds(15));
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (session.ServerExited) throw new IOException("The Android screen server exited. Check OEM debugging permissions and Android compatibility.");
                try
                {
                    session.video = new TcpClient { NoDelay = true };
                    await session.video.ConnectAsync(IPAddress.Loopback, session.forwardPort, deadline.Token);
                    await VideoProtocol.ReadForwardReadyAsync(session.Video, deadline.Token);
                    break;
                }
                catch (Exception e) when (e is SocketException or EndOfStreamException or IOException && !deadline.IsCancellationRequested)
                {
                    session.video?.Dispose(); session.video = null;
                    await Task.Delay(100, deadline.Token);
                }
            }
            // Server waits for both enabled sockets before it writes device/codec metadata.
            session.control = new TcpClient { NoDelay = true };
            await session.control.ConnectAsync(IPAddress.Loopback, session.forwardPort, deadline.Token);
            session.Protocol = new VideoProtocol(session.Video);
            await session.Protocol.ReadPreambleAsync(deadline.Token);
            return session;
        }
        catch { await session.DisposeAsync(); throw; }
    }
    public async ValueTask DisposeAsync()
    {
        video?.Dispose(); control?.Dispose();
        if (server is not null) AdbProcess.Kill(server);
        drains.Cancel();
        try { await Task.WhenAll(output); } catch (OperationCanceledException) { }
        // Bound cleanup even when the phone has left Wi-Fi. Never remove another session's forwards/files.
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        if (adb.IsAlive)
        {
            if (forwardPort != 0)
                try { await adb.Client.SelectedAsync(serial, ["forward", "--remove", $"tcp:{forwardPort}"], cleanup.Token); } catch (Exception) { }
            try { await adb.Client.SelectedAsync(serial, ["shell", "rm", "-f", remote], cleanup.Token); } catch (Exception) { }
        }
        server?.Dispose(); drains.Dispose();
    }
}
