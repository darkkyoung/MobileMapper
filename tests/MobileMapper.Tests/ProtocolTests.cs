using System.Buffers;
using System.Buffers.Binary;
using MobileMapper.Core;
using MobileMapper.Scrcpy;
using Xunit;
namespace MobileMapper.Tests;
public class ProtocolTests
{
    private static byte[] Header(uint flags = 0x80000000, int w = 1280, int h = 720)
    {
        byte[] b = new byte[12]; BinaryPrimitives.WriteUInt32BigEndian(b, flags);
        BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(4), w); BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), h); return b;
    }
    private static byte[] PacketHeader(int length, ulong flags = 0)
    {
        byte[] b = new byte[12]; BinaryPrimitives.WriteUInt64BigEndian(b, flags); BinaryPrimitives.WriteInt32BigEndian(b.AsSpan(8), length); return b;
    }
    [Fact] public async Task EveryByteMayBeFragmented()
    {
        byte[] preamble = new byte[68]; BinaryPrimitives.WriteUInt32BigEndian(preamble.AsSpan(64), 0x68323634);
        using var s = new FragmentedStream(new byte[] {0}.Concat(preamble).Concat(Header()).Concat(PacketHeader(3, (1UL << 62) | 42)).Concat(new byte[] {1, 2, 3}).ToArray());
        await VideoProtocol.ReadForwardReadyAsync(s, default);
        var reader = new VideoProtocol(s); await reader.ReadPreambleAsync(default);
        var geometry = Assert.IsType<GeometryRecord>(await reader.ReadAsync(default)); Assert.Equal(1280, geometry.Geometry.Width);
        using var p = Assert.IsType<EncodedPacket>(await reader.ReadAsync(default));
        Assert.True(p.IsConfig); Assert.Equal(42, p.Pts); Assert.Equal(new byte[] {1, 2, 3}, p.Data.ToArray());
    }
    [Fact] public async Task RotationCreatesNewGeneration()
    {
        var parser = new VideoProtocol(new MemoryStream(Header().Concat(Header(0x80000001, 720, 1280)).ToArray()));
        var first = (GeometryRecord)await parser.ReadAsync(default); var second = (GeometryRecord)await parser.ReadAsync(default);
        Assert.Equal(2, second.Geometry.Generation); Assert.Equal(720, second.Geometry.Width); Assert.NotEqual(first.Geometry, second.Geometry);
    }
    [Theory] [InlineData(0)] [InlineData(-1)] [InlineData(16777217)]
    public async Task MalformedPacketLengthFailsBeforeAllocation(int length)
    {
        var parser = new VideoProtocol(new MemoryStream(Header().Concat(PacketHeader(length)).ToArray()));
        await parser.ReadAsync(default); await Assert.ThrowsAsync<InvalidDataException>(async () => await parser.ReadAsync(default));
    }
    [Fact] public async Task PacketBeforeGeometryFails()
    { await Assert.ThrowsAsync<InvalidDataException>(async () => await new VideoProtocol(new MemoryStream(PacketHeader(1))).ReadAsync(default)); }
    [Theory] [InlineData(0x80000002U, 1280)] [InlineData(0x80000000U, 0)]
    public async Task InvalidSessionFails(uint flags, int w)
    { await Assert.ThrowsAsync<InvalidDataException>(async () => await new VideoProtocol(new MemoryStream(Header(flags, w))).ReadAsync(default)); }
    [Fact] public async Task WrongCodecFails()
    { await Assert.ThrowsAsync<InvalidDataException>(() => new VideoProtocol(new MemoryStream(new byte[68])).ReadPreambleAsync(default)); }
    [Fact] public async Task WrongDummyFails()
    { await Assert.ThrowsAsync<InvalidDataException>(() => VideoProtocol.ReadForwardReadyAsync(new MemoryStream(new byte[] {1}), default)); }
    [Fact] public async Task TruncatedPayloadIsEof()
    {
        var reader = new VideoProtocol(new FragmentedStream(Header().Concat(PacketHeader(3)).Concat(new byte[] {1}).ToArray()));
        await reader.ReadAsync(default); await Assert.ThrowsAsync<EndOfStreamException>(async () => await reader.ReadAsync(default));
    }
    [Fact] public async Task ReadCancellationInterruptsBlockedNetwork()
    {
        using var ct = new CancellationTokenSource(); var task = new VideoProtocol(new BlockingStream()).ReadAsync(ct.Token);
        ct.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
    [Fact] public void TouchGoldenBytes()
    {
        var frame = new TouchFrame(TouchAction.Down, 1, new(.5, .5), new(1280, 720, 1), 1);
        Assert.Equal("020000000000000000010000028000000168050002D0FFFF0000000000000000", Convert.ToHexString(TouchProtocol.Serialize(frame)));
        var up = TouchProtocol.Serialize(frame with { Action = TouchAction.Up }); Assert.Equal(1, up[1]); Assert.Equal(0, up[22]); Assert.Equal(0, up[23]);
    }
    [Fact] public async Task ControlReaderRejectsUnknownMessages()
    { await Assert.ThrowsAsync<InvalidDataException>(() => TouchProtocol.DrainDeviceMessagesAsync(new MemoryStream(new byte[] {255}), default)); }
    [Fact] public async Task ControlReaderBoundsClipboardLength()
    { await Assert.ThrowsAsync<InvalidDataException>(() => TouchProtocol.DrainDeviceMessagesAsync(new MemoryStream(new byte[] {0, 0x7f, 0xff, 0xff, 0xff}), default)); }
    [Fact] public async Task ReleaseDropsUnsentDownInsteadOfOrphanUp()
    {
        using var output = new MemoryStream(); using var writer = new ControlWriter(output); using var ct = new CancellationTokenSource();
        writer.Enqueue(Frame(TouchAction.Down)); var release = writer.ReleaseAllAsync(2);
        var task = writer.RunAsync(ct.Token); await release.WaitAsync(TimeSpan.FromSeconds(2));
        Assert.Empty(output.ToArray()); ct.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }
    [Fact] public async Task ReleaseWaitsForActualDownThenSendsUp()
    {
        using var output = new ObservedStream(); using var writer = new ControlWriter(output); using var ct = new CancellationTokenSource();
        var run = writer.RunAsync(ct.Token); writer.Enqueue(Frame(TouchAction.Down));
        await output.FirstWrite.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await writer.ReleaseAllAsync(2).WaitAsync(TimeSpan.FromSeconds(2));
        byte[] bytes = output.ToArray(); Assert.Equal(64, bytes.Length); Assert.Equal(0, bytes[1]); Assert.Equal(1, bytes[33]);
        Assert.Throws<InvalidOperationException>(() => writer.Enqueue(Frame(TouchAction.Down)));
        ct.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }
    [Fact] public void AdjacentMovesCoalesceWithoutDroppingDown()
    {
        using var w = new ControlWriter(new MemoryStream()); w.Enqueue(Frame(TouchAction.Down));
        for (int i = 0; i < 1000; i++) w.Enqueue(Frame(TouchAction.Move));
        Assert.Equal(2, w.QueueDepth);
    }
    [Fact] public void ControlQueueIsBounded()
    {
        using var w = new ControlWriter(new MemoryStream());
        for (int i = 0; i < 128; i++) w.Enqueue(Frame(TouchAction.Down) with { PointerId = (ulong)i });
        Assert.Throws<IOException>(() => w.Enqueue(Frame(TouchAction.Down)));
    }
    [Fact] public void CompressedQueueIsBoundedAndDisposesRejectedPacket()
    {
        using var q = new PacketQueue();
        for (int i = 0; i < 8; i++) q.Write(new GeometryRecord(new(1280, 720, i + 1)));
        using var packet = new EncodedPacket(ArrayPool<byte>.Shared.Rent(1), 1, 0, false, false, 1);
        Assert.Throws<IOException>(() => q.Write(packet)); Assert.Throws<ObjectDisposedException>(() => packet.Data.ToArray());
    }
    [Fact] public async Task CompressedQueueReadCancels()
    {
        using var q = new PacketQueue(); using var ct = new CancellationTokenSource(); var read = q.ReadAsync(ct.Token).AsTask();
        ct.Cancel(); await Assert.ThrowsAnyAsync<OperationCanceledException>(() => read);
    }
    [Fact] public async Task WrongServerArtifactIsRejected()
    {
        var file = Path.GetTempFileName();
        try { await File.WriteAllTextAsync(file, "not a server"); await Assert.ThrowsAsync<InvalidDataException>(() => ServerArtifact.VerifyAsync(file, default)); }
        finally { File.Delete(file); }
    }
    private static TouchFrame Frame(TouchAction action) => new(action, 1, new(.5, .5), new(1280, 720, 1), 1);
    private sealed class FragmentedStream(byte[] bytes) : MemoryStream(bytes)
    { public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) => base.ReadAsync(buffer[..Math.Min(1, buffer.Length)], ct); }
    private sealed class ObservedStream : MemoryStream
    {
        public TaskCompletionSource FirstWrite { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken ct = default)
        { await base.WriteAsync(buffer, ct); FirstWrite.TrySetResult(); }
    }
    private sealed class BlockingStream : MemoryStream
    { public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default) { await Task.Delay(Timeout.Infinite, ct); return 0; } }
}
