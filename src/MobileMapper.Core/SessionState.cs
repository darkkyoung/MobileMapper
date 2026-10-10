namespace MobileMapper.Core;

public enum SessionState
{
    Idle, Discovering, Pairing, Connecting, StartingServer, Streaming,
    Recovering, NeedsPairing, NeedsUserAction, Stopping
}

public sealed class SessionStateMachine
{
    public SessionState State { get; private set; } = SessionState.Idle;
    public void MoveTo(SessionState next)
    {
        if (State == next) return;
        bool allowed = next == SessionState.Stopping ||
            next is SessionState.NeedsPairing or SessionState.NeedsUserAction ||
            (State, next) switch
            {
                (SessionState.Idle or SessionState.NeedsPairing or SessionState.NeedsUserAction,
                    SessionState.Discovering or SessionState.Pairing or SessionState.Connecting) => true,
                (SessionState.Discovering or SessionState.Pairing, SessionState.Idle) => true,
                (SessionState.Connecting, SessionState.StartingServer) => true,
                (SessionState.StartingServer, SessionState.Streaming) => true,
                (SessionState.Connecting or SessionState.StartingServer or SessionState.Streaming, SessionState.Recovering) => true,
                (SessionState.Recovering, SessionState.Connecting) => true,
                (SessionState.Stopping, SessionState.Idle) => true,
                _ => false
            };
        if (!allowed) throw new InvalidOperationException($"Invalid session transition: {State} -> {next}");
        State = next;
    }
}

public static class RetryPolicy
{
    public const int MaxAttempts = 5;
    public static TimeSpan Delay(int attempt) => TimeSpan.FromSeconds(Math.Min(15, 1 << Math.Clamp(attempt, 0, 4)));
}
