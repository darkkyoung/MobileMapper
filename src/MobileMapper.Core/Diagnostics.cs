namespace MobileMapper.Core;

public sealed class DiagnosticsLog
{
    private readonly Queue<string> entries = [];
    private readonly object sync = new();
    public void Add(string category, string safeMessage)
    {
        // Call sites use fixed messages; never pass process output, input data or credentials.
        lock (sync)
        {
            if (entries.Count == 200) entries.Dequeue();
            entries.Enqueue($"{DateTimeOffset.Now:HH:mm:ss} {category}: {safeMessage}");
        }
    }
    public string Snapshot() { lock (sync) return string.Join(Environment.NewLine, entries); }
}

public sealed class StreamCounters
{
    public long Received;
    public long Reconnects;
    public long QueuedBytes;
    public long QueueDepth;
}
