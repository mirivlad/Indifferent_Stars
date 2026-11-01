using IndifferentStars.Shared;
using IndifferentStars.Shared.World;

namespace IndifferentStars.Tests;

public sealed class RenderFrameTests
{
    [Theory]
    [InlineData(10_000)]
    [InlineData(50_000)]
    [InlineData(100_000)]
    [InlineData(149_597_870_000)]
    public void RebasePreservesRelativeOffsets(double coordinate)
    {
        var player = new Vec3d(coordinate + 0.125, 40, -20);
        var other = player + new Vec3d(0.25, -2, 3);
        var origin = RenderFrame.ChooseOrigin(player, Vec3d.Zero);
        var relativePlayer = player - origin;
        var relativeOther = other - origin;
        Assert.Equal(0.25, relativeOther.X - relativePlayer.X, 8);
        Assert.Equal(-2, relativeOther.Y - relativePlayer.Y, 8);
        Assert.Equal(3, relativeOther.Z - relativePlayer.Z, 8);
        Assert.True(relativePlayer.Length < RenderFrame.RebaseDistance);
    }
}
