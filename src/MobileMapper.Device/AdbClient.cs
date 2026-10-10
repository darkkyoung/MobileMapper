namespace MobileMapper.Device;

public sealed class AdbClient(IAdbRunner runner)
{
    public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, CancellationToken ct, TimeSpan? timeout = null)
        => runner.RunAsync(arguments, null, timeout ?? TimeSpan.FromSeconds(10), ct);
    public async Task<IReadOnlyList<AdbDevice>> DevicesAsync(CancellationToken ct)
    {
        var r = await RunAsync(["devices", "-l"], ct); Check(r);
        return AdbParsing.Devices(r.Output);
    }
    public async Task<IReadOnlyList<MdnsService>> DiscoverAsync(CancellationToken ct)
    {
        var r = await RunAsync(["mdns", "services"], ct); Check(r);
        return AdbParsing.Services(r.Output);
    }
    public async Task PairAsync(Endpoint endpoint, string code, CancellationToken ct)
    {
        if (code.Length != 6 || code.Any(c => c is < '0' or > '9')) throw new FormatException("Enter the six-digit pairing code from the phone.");
        var r = await runner.RunAsync(["pair", endpoint.ToString()], code, TimeSpan.FromSeconds(30), ct);
        if (r.ExitCode != 0 || !r.Output.Contains("Successfully paired", StringComparison.OrdinalIgnoreCase))
            throw new IOException("Pairing failed. Reopen the phone's pairing-code screen and verify the pairing port and code.");
    }
    public async Task ConnectAsync(Endpoint endpoint, CancellationToken ct)
    {
        var r = await RunAsync(["connect", endpoint.ToString()], ct);
        if (r.ExitCode != 0 || !(r.Output.Contains("connected to", StringComparison.OrdinalIgnoreCase) ||
            r.Output.Contains("already connected", StringComparison.OrdinalIgnoreCase)))
            throw new IOException("Connection failed. Use the connection port on the main Wireless Debugging screen, not the pairing port.");
    }
    public Task<ProcessResult> SelectedAsync(string serial, IReadOnlyList<string> arguments, CancellationToken ct, TimeSpan? timeout = null)
    {
        if (!AdbParsing.IsSelector(serial)) throw new ArgumentException("Invalid device selector.");
        return RunAsync(new[] { "-s", serial }.Concat(arguments).ToArray(), ct, timeout);
    }
    public async Task<string> IdentityAsync(string serial, CancellationToken ct)
    {
        var r = await SelectedAsync(serial, ["shell", "getprop", "ro.serialno"], ct); Check(r);
        string identity = r.Output.Trim();
        if (identity.Length is < 1 or > 128 || !AdbParsing.IsSelector(identity))
            throw new IOException("Device identity is unavailable. Select the device again instead of automatically reconnecting.");
        return identity;
    }
    public static void Check(ProcessResult result)
    {
        if (result.ExitCode != 0) throw new IOException("ADB command failed. Check the device authorization and selected Platform-Tools installation.");
    }
}
