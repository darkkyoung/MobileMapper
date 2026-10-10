using System.Security.Cryptography;
using System.Text.Json;

namespace MobileMapper.Scrcpy;

public static class ServerArtifact
{
    private static readonly JsonDocument Manifest = JsonDocument.Parse(
        typeof(ServerArtifact).Assembly.GetManifestResourceStream("dependencies.lock.json")!);
    public static string Version => Manifest.RootElement.GetProperty("scrcpy").GetProperty("version").GetString()!;
    public static string Sha256 => Manifest.RootElement.GetProperty("scrcpy").GetProperty("sha256").GetString()!;
    public static async Task VerifyAsync(string path, CancellationToken ct)
    {
        await using var file = File.OpenRead(path);
        byte[] hash = await SHA256.HashDataAsync(file, ct);
        if (!CryptographicOperations.FixedTimeEquals(hash, Convert.FromHexString(Sha256)))
            throw new InvalidDataException("The scrcpy server file does not match the pinned 5.0.1 SHA-256. Rebuild or re-download the complete developer package.");
    }
}
