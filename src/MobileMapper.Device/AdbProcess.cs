using System.Diagnostics;
using System.Text;

namespace MobileMapper.Device;

public sealed record ProcessResult(int ExitCode, string Output, string Error);
public interface IAdbRunner
{
    Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput,
        TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class AdbProcess(string executable, int serverPort = 5037) : IAdbRunner
{
    public string Executable { get; } = ValidatePath(executable);
    private static string ValidatePath(string path)
    {
        if (!Path.IsPathFullyQualified(path) || !File.Exists(path) ||
            !Path.GetFileName(path).Equals("adb.exe", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Select adb.exe inside the official Windows Platform-Tools folder.");
        return path;
    }
    public ProcessStartInfo CreateStartInfo(IReadOnlyList<string> arguments)
    {
        var info = new ProcessStartInfo(Executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = true
        };
        info.ArgumentList.Add("-P"); info.ArgumentList.Add(serverPort.ToString());
        foreach (var arg in arguments) info.ArgumentList.Add(arg);
        return info;
    }
    public async Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        using var process = new Process { StartInfo = CreateStartInfo(arguments) };
        if (!process.Start()) throw new IOException("Could not start ADB.");
        // Drain continuously but retain at most 32 KiB. Never log this result wholesale.
        var stdout = DrainAsync(process.StandardOutput, deadline.Token);
        var stderr = DrainAsync(process.StandardError, deadline.Token);
        try
        {
            if (standardInput is not null) await process.StandardInput.WriteLineAsync(standardInput.AsMemory(), deadline.Token);
            process.StandardInput.Close();
            await process.WaitForExitAsync(deadline.Token);
            return new(process.ExitCode, await stdout, await stderr);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            Kill(process);
            throw new TimeoutException("ADB operation timed out. Check Wireless Debugging and the Wi-Fi connection.");
        }
        finally
        {
            Kill(process);
            deadline.Cancel();
            try { await Task.WhenAll(stdout, stderr); } catch (OperationCanceledException) { }
        }
    }
    public Process StartServerShell(IReadOnlyList<string> arguments)
    {
        var process = new Process { StartInfo = CreateStartInfo(arguments) };
        if (!process.Start()) throw new IOException("Could not launch the device server.");
        process.StandardInput.Close();
        return process;
    }
    public static async Task<string> DrainAsync(StreamReader reader, CancellationToken ct)
    {
        var text = new StringBuilder();
        char[] buffer = new char[2048];
        int count;
        while ((count = await reader.ReadAsync(buffer, ct)) != 0)
            if (text.Length < 32768) text.Append(buffer, 0, Math.Min(count, 32768 - text.Length));
        return text.ToString();
    }
    public static void Kill(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
        catch (System.ComponentModel.Win32Exception) { }
    }
}
