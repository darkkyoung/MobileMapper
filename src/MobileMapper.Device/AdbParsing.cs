using System.Net;
using System.Text.RegularExpressions;

namespace MobileMapper.Device;

public readonly record struct Endpoint(string Host, int Port)
{
    public static Endpoint Parse(string input)
    {
        input = input.Trim();
        int split = input.LastIndexOf(':');
        if (split < 1 || !int.TryParse(input.AsSpan(split + 1), out int port) || port is < 1 or > 65535)
            throw new FormatException("Enter an IP address and port, for example 192.0.2.1:40000.");
        string host = input[..split];
        if (host.StartsWith('[') && host.EndsWith(']')) host = host[1..^1];
        if (!IPAddress.TryParse(host, out _) &&
            !Regex.IsMatch(host, @"\A(?=.{1,253}\z)[a-zA-Z0-9](?:[a-zA-Z0-9.-]*[a-zA-Z0-9])?\.local\z"))
            throw new FormatException("Use an IP address or a discovered .local hostname.");
        return new(host, port);
    }
    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";
}

public sealed record AdbDevice(string Serial, string State, string Model)
{
    public override string ToString() => $"{Model} — {Serial} ({State})";
}
public sealed record MdnsService(string Name, bool IsPairing, Endpoint Endpoint)
{
    public override string ToString() => $"{Name} — {Endpoint}";
}

public static class AdbParsing
{
    public static IReadOnlyList<AdbDevice> Devices(string text)
    {
        var result = new List<AdbDevice>();
        foreach (string line in text.Split('\n'))
        {
            var parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || parts[0] is "List" or "*" || !IsSelector(parts[0])) continue;
            if (parts[1] is not ("device" or "offline" or "unauthorized")) continue;
            result.Add(new(parts[0], parts[1], parts.FirstOrDefault(p => p.StartsWith("model:"))?[6..] ?? "Android"));
        }
        return result;
    }
    public static IReadOnlyList<MdnsService> Services(string text)
    {
        var result = new List<MdnsService>();
        foreach (string line in text.Split('\n'))
        {
            var p = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (p.Length < 3) continue;
            string kind = p[1].TrimEnd('.');
            if (kind is not ("_adb-tls-pairing._tcp" or "_adb-tls-connect._tcp")) continue;
            try { result.Add(new(p[0], kind == "_adb-tls-pairing._tcp", Endpoint.Parse(p[2]))); }
            catch (FormatException) { /* Unsupported discovery record; manual fallback remains available. */ }
        }
        return result;
    }
    public static bool IsSelector(string serial) => Regex.IsMatch(serial, @"\A[a-zA-Z0-9_:.\[\]%-]+\z");
}
