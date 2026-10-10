using System.Threading.Channels;
namespace MobileMapper.Scrcpy;

/// <summary>Never drop arbitrary H.264 packets. Saturation fails the session and requests a fresh decoder/server.</summary>
public sealed class PacketQueue : IDisposable
{
    private readonly Channel<StreamRecord> queue = Channel.CreateBounded<StreamRecord>(new BoundedChannelOptions(8)
        { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private long bytes;
    public long Bytes => Interlocked.Read(ref bytes);
    public int Count => queue.Reader.Count;
    public void Write(StreamRecord record)
    {
        int size = (record as EncodedPacket)?.Length ?? 0;
        long total = Interlocked.Add(ref bytes, size);
        if (total > VideoProtocol.MaximumPacketSize || !queue.Writer.TryWrite(record))
        {
            Interlocked.Add(ref bytes, -size);
            (record as IDisposable)?.Dispose();
            throw new IOException("Compressed video backlog exceeded its limit; restarting the stream.");
        }
    }
    public async ValueTask<StreamRecord> ReadAsync(CancellationToken ct)
    {
        var record = await queue.Reader.ReadAsync(ct);
        Interlocked.Add(ref bytes, -((record as EncodedPacket)?.Length ?? 0));
        return record;
    }
    public void Dispose()
    {
        queue.Writer.TryComplete();
        while (queue.Reader.TryRead(out var record)) (record as IDisposable)?.Dispose();
        Interlocked.Exchange(ref bytes, 0);
    }
}
