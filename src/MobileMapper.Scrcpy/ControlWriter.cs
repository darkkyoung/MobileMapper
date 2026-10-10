using MobileMapper.Core;

namespace MobileMapper.Scrcpy;

// One socket writer. Release is a priority barrier: drop unsent intent, then release only sent IDs.
public sealed class ControlWriter(Stream stream) : IDisposable
{
    private sealed record Command(TouchFrame? Frame, TaskCompletionSource? Barrier);
    private readonly LinkedList<Command> queue = [];
    private readonly Dictionary<ulong, TouchFrame> sent = [];
    private readonly SemaphoreSlim available = new(0, 1);
    private readonly object sync = new();
    private long generation = 1;
    private bool closed;
    public int QueueDepth { get { lock (sync) return queue.Count; } }
    public void Enqueue(TouchFrame frame)
    {
        lock (sync)
        {
            if (closed || frame.InputGeneration != generation) throw new InvalidOperationException("Stale or closed touch session.");
            if (frame.Action == TouchAction.Move && queue.Last?.Value.Frame is { } last &&
                last.Action == TouchAction.Move && last.PointerId == frame.PointerId)
                queue.Last.Value = new(frame, null);
            else
            {
                if (queue.Count >= 128) throw new IOException("Touch queue saturated; release and reconnect are required.");
                queue.AddLast(new Command(frame, null));
            }
            Signal();
        }
    }
    public Task ReleaseAllAsync(long newGeneration)
    {
        lock (sync)
        {
            if (closed) return Task.CompletedTask;
            generation = newGeneration;
            // Preserve existing barrier completions when focus-loss and Disconnect race.
            foreach (var command in queue) command.Barrier?.TrySetCanceled();
            queue.Clear();
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            queue.AddLast(new Command(null, completion)); Signal();
            return completion.Task;
        }
    }
    private void Signal() { if (available.CurrentCount == 0) available.Release(); }
    public async Task RunAsync(CancellationToken ct)
    {
        try
        {
            while (true)
            {
                await available.WaitAsync(ct);
                while (true)
                {
                    Command? command;
                    lock (sync)
                    {
                        if (queue.First is null) break;
                        command = queue.First.Value; queue.RemoveFirst();
                    }
                    if (command.Barrier is { } barrier)
                    {
                        try
                        {
                            foreach (var contact in sent.Values.ToArray())
                                await WriteAsync(contact with { Action = TouchAction.Up }, ct);
                            sent.Clear(); barrier.TrySetResult();
                        }
                        catch (Exception ex) { barrier.TrySetException(ex); throw; }
                    }
                    else if (command.Frame is { } frame)
                    {
                        lock (sync) { if (frame.InputGeneration != generation) continue; }
                        await WriteAsync(frame, ct);
                        if (frame.Action == TouchAction.Up) sent.Remove(frame.PointerId);
                        else sent[frame.PointerId] = frame;
                    }
                }
            }
        }
        finally
        {
            lock (sync)
            {
                closed = true;
                foreach (var c in queue) c.Barrier?.TrySetCanceled();
                queue.Clear();
            }
        }
    }
    private async Task WriteAsync(TouchFrame frame, CancellationToken ct)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMilliseconds(500));
        await stream.WriteAsync(TouchProtocol.Serialize(frame), deadline.Token);
    }
    public void Dispose() => available.Dispose(); // Only after RunAsync has finished.
}
