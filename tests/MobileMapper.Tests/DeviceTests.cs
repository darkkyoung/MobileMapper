using MobileMapper.Device;
using Xunit;
namespace MobileMapper.Tests;
public class DeviceTests
{
    [Theory]
    [InlineData("192.0.2.1:12345", "192.0.2.1:12345")]
    [InlineData("[::1]:4321", "[::1]:4321")]
    [InlineData("phone.local:5555", "phone.local:5555")]
    public void EndpointRoundTrip(string text, string expected) => Assert.Equal(expected, Endpoint.Parse(text).ToString());
    [Theory]
    [InlineData("192.0.2.1")] [InlineData("192.0.2.1:0")] [InlineData("192.0.2.1:65536")]
    [InlineData("host;command:1234")] [InlineData("-a:1234")] [InlineData("192.0.2.1:abc")]
    public void InvalidEndpointFails(string text) => Assert.Throws<FormatException>(() => Endpoint.Parse(text));
    [Fact] public void DevicesKeepAuthorizationStates()
    {
        var devices = AdbParsing.Devices("List of devices attached\r\n192.0.2.1:10000 device product:x model:Test_Phone transport_id:2\nserial2 offline\nserial3 unauthorized\n* daemon started successfully *\n");
        Assert.Equal(3, devices.Count); Assert.Equal("Test_Phone", devices[0].Model);
        Assert.Equal("unauthorized", devices[2].State);
    }
    [Fact] public void DiscoverySeparatesPairAndConnectPorts()
    {
        var found = AdbParsing.Services("List of discovered mdns services\nphone _adb-tls-pairing._tcp. 192.0.2.1:1234\nphone _adb-tls-connect._tcp. 192.0.2.1:5678 extra\nbad _adb-tls-connect._tcp invalid\n");
        Assert.Equal(2, found.Count); Assert.True(found[0].IsPairing); Assert.False(found[1].IsPairing);
        Assert.NotEqual(found[0].Endpoint.Port, found[1].Endpoint.Port);
    }
    [Fact] public async Task PairingCodeUsesOnlyStdin()
    {
        var runner = new FakeRunner(new(0, "Successfully paired", ""));
        await new AdbClient(runner).PairAsync(Endpoint.Parse("192.0.2.1:1234"), "000000", default);
        Assert.Equal(new[] {"pair", "192.0.2.1:1234"}, runner.Arguments);
        Assert.Equal("000000", runner.Input); // Synthetic test value, never used against a device.
    }
    [Fact] public async Task PairFailureDoesNotExposeProcessOutput()
    {
        var client = new AdbClient(new FakeRunner(new(1, "sensitive sentinel", "sensitive sentinel")));
        var e = await Assert.ThrowsAsync<IOException>(() => client.PairAsync(Endpoint.Parse("192.0.2.1:1234"), "000000", default));
        Assert.DoesNotContain("sentinel", e.Message);
    }
    [Theory] [InlineData("123")] [InlineData("abcdef")] [InlineData("1234567")]
    public async Task InvalidCodeNeverStartsAdb(string code)
    {
        var fake = new FakeRunner(new(0, "", ""));
        await Assert.ThrowsAsync<FormatException>(() => new AdbClient(fake).PairAsync(Endpoint.Parse("192.0.2.1:1234"), code, default));
        Assert.Empty(fake.Arguments);
    }
    [Fact] public async Task DeviceCommandHasExplicitSelector()
    {
        var fake = new FakeRunner(new(0, "device", ""));
        await new AdbClient(fake).SelectedAsync("192.0.2.1:1234", ["get-state"], default);
        Assert.Equal(new[] {"-s", "192.0.2.1:1234", "get-state"}, fake.Arguments);
    }
    [Fact] public void SafeArgumentListAndExplicitPath()
    {
        string folder = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()); Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, "adb.exe"); File.WriteAllText(path, "");
        try {
            var start = new AdbProcess(path, 12345).CreateStartInfo(["pair", "192.0.2.1:1234"]);
            Assert.False(start.UseShellExecute); Assert.Empty(start.Arguments);
            Assert.Equal(new[] {"-P", "12345", "pair", "192.0.2.1:1234"}, start.ArgumentList);
            Assert.Throws<ArgumentException>(() => new AdbProcess("adb.exe"));
        } finally { Directory.Delete(folder, true); }
    }
    private sealed class FakeRunner(ProcessResult result) : IAdbRunner
    {
        public IReadOnlyList<string> Arguments { get; private set; } = [];
        public string? Input { get; private set; }
        public Task<ProcessResult> RunAsync(IReadOnlyList<string> arguments, string? standardInput, TimeSpan timeout, CancellationToken ct)
        { Arguments = arguments; Input = standardInput; ct.ThrowIfCancellationRequested(); return Task.FromResult(result); }
    }
}
