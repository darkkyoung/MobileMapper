using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace MobileMapper.Device;

// Own a foreground daemon process, not Android Studio's daemon. Never issue kill-server.
public sealed class AdbRuntime : IAsyncDisposable
{
    private readonly Process daemon;
    private readonly CancellationTokenSource lifetime = new();
    private readonly Task<string> output;
    private readonly Task<string> error;
    public AdbProcess Runner { get; }
    public AdbClient Client { get; }
    private AdbRuntime(string executable, int port, Process process)
    {
        daemon = process;
        Runner = new AdbProcess(executable, port);
        Client = new(Runner);
        output = AdbProcess.DrainAsync(daemon.StandardOutput, lifetime.Token);
        error = AdbProcess.DrainAsync(daemon.StandardError, lifetime.Token);
    }
    public static async Task<AdbRuntime> StartAsync(string executable, CancellationToken ct)
    {
        _ = new AdbProcess(executable); // Validate the explicit path before launching anything.
        for (int attempt = 0; attempt < 3; attempt++)
        {
            var reservation = new TcpListener(IPAddress.Loopback, 0);
            reservation.Start();
            int port = ((IPEndPoint)reservation.LocalEndpoint).Port;
            reservation.Stop();
            var info = new ProcessStartInfo(executable)
            {
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in new[] { "-L", $"tcp:127.0.0.1:{port}", "server", "nodaemon" }) info.ArgumentList.Add(arg);
            var process = Process.Start(info) ?? throw new IOException("Could not start the app-owned ADB daemon.");
            var runtime = new AdbRuntime(executable, port, process);
            try
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
                deadline.CancelAfter(TimeSpan.FromSeconds(8));
                while (!process.HasExited)
                {
                    try
                    {
                        using var socket = new TcpClient();
                        await socket.ConnectAsync(IPAddress.Loopback, port, deadline.Token);
                        if (!process.HasExited) return runtime;
                    }
                    catch (SocketException) { }
                    await Task.Delay(100, deadline.Token);
                }
            }
            catch { await runtime.DisposeAsync(); throw; }
            await runtime.DisposeAsync(); // Bind collision/failed daemon: never target a foreign process.
        }
        throw new IOException("Cannot start a private ADB daemon. Check the selected official Platform-Tools installation.");
    }
    public bool IsAlive => !daemon.HasExited;
    public async ValueTask DisposeAsync()
    {
        AdbProcess.Kill(daemon);
        lifetime.Cancel();
        try { await Task.WhenAll(output, error); } catch (OperationCanceledException) { }
        daemon.Dispose(); lifetime.Dispose();
    }
}
