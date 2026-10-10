using System.Buffers.Binary;
using MobileMapper.Core;

namespace MobileMapper.Scrcpy;

public static class TouchProtocol
{
    public static byte[] Serialize(TouchFrame frame)
    {
        if (frame.Action is not (TouchAction.Down or TouchAction.Move or TouchAction.Up) || frame.PointerId >= 1UL << 63)
            throw new ArgumentException("Invalid finger contact.");
        var (x, y) = frame.Position.ToPixels(frame.Geometry);
        byte[] data = new byte[32]; data[0] = 2; data[1] = (byte)frame.Action;
        BinaryPrimitives.WriteUInt64BigEndian(data.AsSpan(2), frame.PointerId);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(10), x);
        BinaryPrimitives.WriteInt32BigEndian(data.AsSpan(14), y);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(18), (ushort)frame.Geometry.Width);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(20), (ushort)frame.Geometry.Height);
        BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(22), frame.Action == TouchAction.Up ? (ushort)0 : ushort.MaxValue);
        return data;
    }
    public static async Task DrainDeviceMessagesAsync(Stream stream, CancellationToken ct)
    {
        byte[] header = new byte[8];
        byte[] discard = new byte[4096];
        while (true)
        {
            await stream.ReadExactlyAsync(header.AsMemory(0, 1), ct);
            int length;
            switch (header[0])
            {
                case 0: // Clipboard: discard without decoding, logging or exporting user content.
                    await stream.ReadExactlyAsync(header.AsMemory(0, 4), ct);
                    length = BinaryPrimitives.ReadInt32BigEndian(header);
                    if (length is < 0 or > 262139) throw new InvalidDataException("Invalid control message length.");
                    break;
                case 1: await stream.ReadExactlyAsync(header, ct); continue;
                case 2:
                    await stream.ReadExactlyAsync(header.AsMemory(0, 4), ct);
                    length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(2));
                    break;
                default: throw new InvalidDataException("Unknown scrcpy device message.");
            }
            while (length > 0)
            {
                int count = Math.Min(length, discard.Length);
                await stream.ReadExactlyAsync(discard.AsMemory(0, count), ct); length -= count;
            }
        }
    }
}
