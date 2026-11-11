using WalkerEngine;

namespace WalkerEngine.Tests;

public class RoutePilotTests
{
    private static readonly MapBounds Square = new(0, 0, 99, 99);
    private static readonly MapPoint[] East = [new(0, 0), new(1, 0)];

    [Fact]
    public void OnRoute_ClicksForward()
    {
        var pilot = new RoutePilot(Tuning());
        var command = pilot.Advance(true, new MapPoint(0.2, 0), Square, East);

        Assert.True(command.Click);
        Assert.False(command.Correcting);
        Assert.False(command.Arrived);
        Assert.InRange(command.OffsetX, 90, 110);
        Assert.InRange(command.OffsetY, -5, 5);
    }

    [Fact]
    public void BelowRoute_CorrectsUpTowardThePath()
    {
        var pilot = new RoutePilot(Tuning());
        var command = pilot.Advance(true, new MapPoint(0.5, 0.25), Square, East);

        Assert.True(command.Click);
        Assert.True(command.Correcting);
        Assert.True(command.OffsetX > 0);
        Assert.True(command.OffsetY < 0);
        Assert.True(command.CrossTrackPixels > 0);
    }

    [Fact]
    public void AtFinish_DoesNotClick()
    {
        var pilot = new RoutePilot(Tuning());
        var command = pilot.Advance(true, new MapPoint(1, 0), Square, East);

        Assert.False(command.Click);
        Assert.True(command.Arrived);
    }

    [Fact]
    public void MissingMarker_DoesNotClick()
    {
        var pilot = new RoutePilot(Tuning());
        var command = pilot.Advance(false, new MapPoint(0.2, 0), Square, East);

        Assert.False(command.Click);
        Assert.Contains("не найден", command.Reason);
    }

    [Fact]
    public void StoppedOnRoute_RepeatsForwardClick()
    {
        var pilot = new RoutePilot(Tuning(stuckSamples: 2));
        var place = new MapPoint(0.2, 0);
        pilot.Advance(true, place, Square, East);
        pilot.Advance(true, place, Square, East);
        var command = pilot.Advance(true, place, Square, East);

        Assert.True(command.Stuck);
        Assert.True(command.Click);
        Assert.False(command.Correcting);
        Assert.InRange(command.OffsetX, 90, 110);
    }

    [Fact]
    public void MovingDown_CorrectsBackToEastRoute()
    {
        var pilot = new RoutePilot(Tuning());
        pilot.Advance(true, new MapPoint(0.3, 0), Square, East);
        var command = pilot.Advance(true, new MapPoint(0.3, 0.05), Square, East);

        Assert.True(command.Correcting);
        Assert.True(command.Click);
    }

    [Fact]
    public void Corner_AimsAtTheNextPointRatherThanTheFinish()
    {
        MapPoint[] route = [new(0, 0), new(0.5, 0), new(0.5, 1)];
        var pilot = new RoutePilot(Tuning());
        var command = pilot.Advance(true, new MapPoint(0.05, 0), Square, route);

        Assert.True(command.Click);
        Assert.False(command.Arrived);
        Assert.Equal(0, command.SegmentIndex);
        Assert.InRange(command.OffsetX, 90, 110);
        Assert.InRange(command.OffsetY, -5, 5);
    }

    [Fact]
    public void Corner_AfterThePoint_TurnsOntoTheNextLeg()
    {
        MapPoint[] route = [new(0, 0), new(0.5, 0), new(0.5, 1)];
        var pilot = new RoutePilot(Tuning());
        pilot.Advance(true, new MapPoint(0.05, 0), Square, route);
        var command = pilot.Advance(true, new MapPoint(0.5, 0), Square, route);

        Assert.True(command.Click);
        Assert.False(command.Arrived);
        Assert.Equal(1, command.SegmentIndex);
        Assert.InRange(command.OffsetX, -5, 5);
        Assert.InRange(command.OffsetY, 90, 110);
    }

    [Fact]
    public void Start_JoinsTheNearestForwardPoint()
    {
        MapPoint[] route = [new(0, 0), new(0.5, 0), new(0.5, 0.5), new(1, 0.5)];
        var pilot = new RoutePilot(Tuning());
        var command = pilot.Advance(true, new MapPoint(0.5, 0.2), Square, route);

        Assert.True(command.Click);
        Assert.False(command.Arrived);
        Assert.InRange(command.OffsetX, -5, 5);
        Assert.True(command.OffsetY > 0);
    }

    [Fact]
    public void ResumeAtCheckpoint_DoesNotJumpToNearestLaterSegment()
    {
        // Петля: после сдвига позиция ближе к позднему сегменту, но checkpoint сохраняем.
        MapPoint[] route =
        [
            new(0, 0),
            new(0.3, 0),
            new(0.3, 0.3),
            new(0.6, 0.3),
            new(0.6, 0),
            new(1, 0),
        ];
        var pilot = new RoutePilot(Tuning());
        pilot.ResumeAtCheckpoint(2); // цель — route[2] = (0.3, 0.3)
        var command = pilot.Advance(true, new MapPoint(0.62, 0.05), Square, route);

        Assert.True(command.Click);
        Assert.False(command.Arrived);
        Assert.Equal(1, command.SegmentIndex);
    }

    [Fact]
    public void Start_BeforeTheRoute_AimsAtTheFirstPoint()
    {
        var pilot = new RoutePilot(Tuning());
        var command = pilot.Advance(true, new MapPoint(-0.2, 0), Square, East);

        Assert.True(command.Click);
        Assert.False(command.Arrived);
        Assert.Equal(-1, command.SegmentIndex);
        Assert.True(command.OffsetX > 0);
        Assert.InRange(command.OffsetY, -5, 5);
    }

    [Fact]
    public void CloserToTheFinish_DoesNotSkipTheUncheckedPoint()
    {
        MapPoint[] route = [new(0, 0), new(0.4, 0), new(0.4, 0.5)];
        var pilot = new RoutePilot(Tuning());
        pilot.Advance(true, new MapPoint(0.05, 0), Square, route);
        var command = pilot.Advance(true, new MapPoint(0.4, 0.2), Square, route);

        Assert.True(command.Click);
        Assert.False(command.Arrived);
        Assert.InRange(command.OffsetX, -5, 5);
        Assert.True(command.OffsetY < 0);
    }

    [Fact]
    public void Rotation_TurnsRightIntoDown()
    {
        var pilot = new RoutePilot(Tuning(rotation: 90));
        var command = pilot.Advance(true, new MapPoint(0.2, 0), Square, East);

        Assert.True(command.Click);
        Assert.InRange(command.OffsetX, -5, 5);
        Assert.InRange(command.OffsetY, 90, 110);
    }

    private static PilotTuning Tuning(int stuckSamples = 3, double rotation = 0) => new()
    {
        LookAheadPixels = 40,
        RejoinLookAheadPixels = 20,
        CorrectionDistancePixels = 16,
        ArrivalDistancePixels = 10,
        StuckMovePixels = 2,
        StuckSampleCount = stuckSamples,
        HeadingToleranceDegrees = 55,
        ClickDistancePixels = 100,
        RotationDegrees = rotation,
    };
}
