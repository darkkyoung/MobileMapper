using System.Buffers;
using System.Buffers.Binary;
using MobileMapper.Core;

namespace MobileMapper.Scrcpy;

public abstract record StreamRecord;
public sealed record GeometryRecord(VideoGeometry Geometry) : StreamRecord;
public sealed record EncodedPacket : StreamRecord, IDisposable
{
    private byte[]? bytes;
    public int Length { get; }
    public long Pts { get; }
    public bool IsConfig { get; }
    public bool IsKeyFrame { get; }
    public long Generation { get; }
    public long ReceivedAt { get; } = System.Diagnostics.Stopwatch.GetTimestamp();
    public ReadOnlySpan<byte> Data => (bytes ?? throw new ObjectDisposedException(nameof(EncodedPacket))).AsSpan(0, Length);
    public EncodedPacket(byte[] pooledBytes, int length, long pts, bool config, bool keyframe, long generation)
        => (bytes, Length, Pts, IsConfig, IsKeyFrame, Generation) = (pooledBytes, length, pts, config, keyframe, generation);
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref bytes, null);
        if (owned is not null) ArrayPool<byte>.Shared.Return(owned);
    }
}

public sealed class VideoProtocol(Stream stream)
{
    public const int MaximumPacketSize = 16 * 1024 * 1024;
    private long generation;
    private readonly byte[] header = new byte[12];
    public static async Task ReadForwardReadyAsync(Stream stream, CancellationToken ct)
    {
        byte[] ready = new byte[1];
        await stream.ReadExactlyAsync(ready, ct);
        if (ready[0] != 0) throw new InvalidDataException("Invalid scrcpy readiness byte.");
    }
    // Called only AFTER the control socket has connected. Otherwise bootstrap deadlocks.
    public async Task ReadPreambleAsync(CancellationToken ct)
    {
        byte[] metadata = new byte[68];
        await stream.ReadExactlyAsync(metadata, ct);
        if (BinaryPrimitives.ReadUInt32BigEndian(metadata.AsSpan(64)) != 0x68323634)
            throw new InvalidDataException("The scrcpy stream did not negotiate H.264.");
        // Device name is intentionally not retained or logged.
    }
    public async Task<StreamRecord> ReadAsync(CancellationToken ct)
    {
        await stream.ReadExactlyAsync(header, ct);
        if ((header[0] & 0x80) != 0)
        {
            uint flags = BinaryPrimitives.ReadUInt32BigEndian(header);
            if ((flags & ~0x80000001U) != 0) throw new InvalidDataException("Unsupported scrcpy session flags.");
            var geometry = new VideoGeometry(BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(4)),
                BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8)), ++generation);
            try { geometry.Validate(); } catch (ArgumentOutOfRangeException e) { throw new InvalidDataException("Invalid video geometry.", e); }
            return new GeometryRecord(geometry);
        }
        ulong flagsPts = BinaryPrimitives.ReadUInt64BigEndian(header);
        int length = BinaryPrimitives.ReadInt32BigEndian(header.AsSpan(8));
        if (generation == 0 || length is < 1 or > MaximumPacketSize) throw new InvalidDataException("Invalid video packet size or missing session header.");
        byte[] data = ArrayPool<byte>.Shared.Rent(length);
        try { await stream.ReadExactlyAsync(data.AsMemory(0, length), ct); }
        catch { ArrayPool<byte>.Shared.Return(data); throw; }
        return new EncodedPacket(data, length, (long)(flagsPts & ((1UL << 61) - 1)),
            (flagsPts & (1UL << 62)) != 0, (flagsPts & (1UL << 61)) != 0, generation);
    }
}
