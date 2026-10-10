using MobileMapper.Core;
using Xunit;
namespace MobileMapper.Tests;

public class CoreTests
{
    private static readonly VideoGeometry Landscape = new(1280, 720, 1);
    [Theory]
    [InlineData(500, 500, 1000, 1000, .5, .5)]
    [InlineData(250, 500, 1000, 1000, .25, .5)]
    [InlineData(1500, 900, 3000, 1800, .5, .5)]
    public void LetterboxMapsContent(double x, double y, double w, double h, double nx, double ny)
    {
        var point = Viewport.Map(x, y, w, h, Landscape)!.Value;
        Assert.Equal(nx, point.X, 6); Assert.Equal(ny, point.Y, 6);
    }
    [Theory]
    [InlineData(500, 0)] [InlineData(500, 999)] [InlineData(-1, 500)] [InlineData(1000, 500)]
    public void LetterboxAndOutsideDoNotInject(double x, double y) => Assert.Null(Viewport.Map(x, y, 1000, 1000, Landscape));
    [Fact] public void PortraitAndDpiUseSameNormalizedPoint()
    {
        var g = new VideoGeometry(720, 1280, 2);
        Assert.Equal(Viewport.Map(500, 300, 1000, 1000, g), Viewport.Map(750, 450, 1500, 1500, g));
    }
    [Fact] public void InclusiveNormalizedEdgeClampsToLastPixel() => Assert.Equal((1279, 719), new NormalizedPoint(1, 1).ToPixels(Landscape));
    [Theory]
    [InlineData(double.NaN)] [InlineData(double.PositiveInfinity)] [InlineData(-.01)] [InlineData(1.01)]
    public void InvalidNormalizedCoordinateFails(double x) => Assert.Throws<ArgumentOutOfRangeException>(() => new NormalizedPoint(x, .5).ToPixels(Landscape));
    [Fact] public void MinimizedViewportDoesNotMap() => Assert.Null(Viewport.Map(0, 0, 0, 0, Landscape));
    [Theory]
    [InlineData(0, 720)] [InlineData(1280, 0)] [InlineData(8193, 720)]
    public void GeometryBounds(int w, int h) => Assert.Throws<ArgumentOutOfRangeException>(() => new VideoGeometry(w, h, 1).Validate());
    [Fact] public void ThreeContactsHaveIndependentIdsAndReleaseTogether()
    {
        var state = new TouchState(); state.Arm();
        var a = state.Down("A", new(.2, .5), Landscape, state.Generation);
        var b = state.Down("B", new(.5, .5), Landscape, state.Generation);
        var c = state.Down("C", new(.8, .5), Landscape, state.Generation);
        Assert.Equal(3, new[] {a.PointerId, b.PointerId, c.PointerId}.Distinct().Count());
        Assert.Equal(b.PointerId, state.Move("B", new(.6, .5), Landscape, state.Generation)!.Value.PointerId);
        var release = state.ReleaseAll(); Assert.Equal(3, release.Count);
        Assert.All(release, frame => Assert.Equal(TouchAction.Up, frame.Action));
        Assert.Equal(0, state.Count); Assert.False(state.Enabled);
    }
    [Fact] public void StaleInputCannotReplayAfterRearm()
    {
        var state = new TouchState(); state.Arm(); long old = state.Generation;
        var first = state.Down("A", new(.5, .5), Landscape, old);
        state.ReleaseAll(); state.Arm();
        Assert.Throws<InvalidOperationException>(() => state.Down("A", new(.5, .5), Landscape, old));
        var second = state.Down("A", new(.5, .5), Landscape, state.Generation);
        Assert.NotEqual(first.PointerId, second.PointerId);
    }
    [Fact] public void DuplicateAndCapacityAreRejected()
    {
        var s = new TouchState(); s.Arm();
        for (int i = 0; i < 10; i++) s.Down(i.ToString(), new(.5, .5), Landscape, s.Generation);
        Assert.Throws<InvalidOperationException>(() => s.Down("0", new(.5, .5), Landscape, s.Generation));
        Assert.Throws<InvalidOperationException>(() => s.Down("11", new(.5, .5), Landscape, s.Generation));
    }
    [Fact] public void ChangedGeometryCannotMoveHeldPointer()
    {
        var s = new TouchState(); s.Arm(); s.Down("A", new(.5, .5), Landscape, s.Generation);
        Assert.Throws<InvalidOperationException>(() => s.Move("A", new(.5, .5), Landscape with { Generation = 2 }, s.Generation));
    }
    [Fact] public void UpOnlyRemovesItsOwnerAndIsIdempotent()
    {
        var s = new TouchState(); s.Arm(); s.Down("A", new(.5, .5), Landscape, s.Generation); s.Down("B", new(.5, .5), Landscape, s.Generation);
        Assert.NotNull(s.Up("A", s.Generation)); Assert.Null(s.Up("A", s.Generation)); Assert.Equal(1, s.Count);
    }
    [Fact] public void StateLifecycleAndExplicitStop()
    {
        var s = new SessionStateMachine();
        foreach (var next in new[] {SessionState.Pairing, SessionState.Idle, SessionState.Discovering, SessionState.Idle,
            SessionState.Connecting, SessionState.StartingServer, SessionState.Streaming, SessionState.Recovering,
            SessionState.Connecting, SessionState.StartingServer, SessionState.Streaming, SessionState.Stopping, SessionState.Idle}) s.MoveTo(next);
        Assert.Equal(SessionState.Idle, s.State);
        Assert.Throws<InvalidOperationException>(() => s.MoveTo(SessionState.Streaming));
    }
    [Fact] public void RetryIsCapped() { Assert.Equal(5, RetryPolicy.MaxAttempts); Assert.Equal(TimeSpan.FromSeconds(15), RetryPolicy.Delay(100)); }
    [Fact] public void LogsAreBounded()
    {
        var log = new DiagnosticsLog(); for (int i = 0; i < 500; i++) log.Add("state", "fixed message");
        Assert.Equal(200, log.Snapshot().Split(Environment.NewLine).Length);
    }
}
