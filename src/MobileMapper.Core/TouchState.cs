namespace MobileMapper.Core;

public enum TouchAction : byte { Down = 0, Up = 1, Move = 2 }
public readonly record struct TouchFrame(TouchAction Action, ulong PointerId, NormalizedPoint Position,
    VideoGeometry Geometry, long InputGeneration);

// Called by the session's input lock; transport serialization belongs elsewhere.
public sealed class TouchState
{
    private readonly Dictionary<string, TouchFrame> contacts = [];
    private ulong nextId = 1;
    public long Generation { get; private set; } = 1;
    public int Count => contacts.Count;
    public bool Enabled { get; private set; }
    public void Arm() => Enabled = true;
    public TouchFrame Down(string owner, NormalizedPoint point, VideoGeometry geometry, long expectedGeneration)
    {
        Check(expectedGeneration);
        point.ToPixels(geometry);
        if (contacts.ContainsKey(owner) || contacts.Count >= 10) throw new InvalidOperationException("Contact is already held or all ten IDs are in use.");
        var frame = new TouchFrame(TouchAction.Down, nextId++, point, geometry, Generation);
        contacts.Add(owner, frame);
        return frame;
    }
    public TouchFrame? Move(string owner, NormalizedPoint point, VideoGeometry geometry, long expectedGeneration)
    {
        Check(expectedGeneration);
        point.ToPixels(geometry);
        if (!contacts.TryGetValue(owner, out var held)) return null;
        if (held.Geometry != geometry) throw new InvalidOperationException("Stale touch geometry.");
        var moved = held with { Action = TouchAction.Move, Position = point };
        contacts[owner] = moved;
        return moved;
    }
    public TouchFrame? Up(string owner, long expectedGeneration)
    {
        Check(expectedGeneration);
        return contacts.Remove(owner, out var held) ? held with { Action = TouchAction.Up } : null;
    }
    public IReadOnlyList<TouchFrame> ReleaseAll()
    {
        var result = contacts.Values.Select(v => v with { Action = TouchAction.Up }).ToArray();
        contacts.Clear();
        Enabled = false;
        Generation++;
        return result;
    }
    private void Check(long expected)
    {
        if (!Enabled || expected != Generation) throw new InvalidOperationException("Touch input is paused or belongs to a previous session.");
    }
}
