using System;
using DoodleSharp.Animation;
using C2VGeometry;

namespace DoodleSharp.Tests;

/// <summary>
/// Timing behaviour of animations that have not started yet.
///
/// <para>
/// <c>Timeline.Update</c> deliberately passes a <b>negative</b> <c>t</c> to an animation whose turn
/// has not come, so it can avoid capturing its initial state early. Any easing applied to that value
/// must be clamped first: the even-powered easings map a negative input to a positive output
/// (<c>EaseInQuad(-0.5) == 0.25</c>), so an unclamped animation applied part of its effect before its
/// own start time.
/// </para>
/// </summary>
[Collection("CanvasState")]
public class AnimationTimingTests
{
    [Fact]
    public void EaseInQuadTurnsANegativeTimeIntoAPositiveOne()
    {
        // The premise. If this ever stops holding, the clamps below are still harmless.
        Assert.True(EasingFunctions.EaseInQuad(-0.5) > 0);
    }

    [Fact]
    public void DrawAnimationStaysHiddenBeforeItStarts()
    {
        var circle = new VCircle(0, 0, 10);
        var animation = new DrawAnimation(circle, 1.0) { EasingFunction = EasingFunctions.EaseInQuad };

        animation.Apply(-0.5);

        Assert.Equal(0, circle.DrawFactor);
    }

    [Fact]
    public void FadeInStaysTransparentBeforeItStarts()
    {
        var circle = new VCircle(0, 0, 10);
        var animation = new FadeInAnimation(circle, 1.0) { EasingFunction = EasingFunctions.EaseInQuad };

        animation.Apply(-0.5);

        Assert.Equal(0, circle.Opacity);
    }

    [Fact]
    public void FadeOutStaysOpaqueBeforeItStarts()
    {
        var circle = new VCircle(0, 0, 10);
        var animation = new FadeOutAnimation(circle, 1.0) { EasingFunction = EasingFunctions.EaseInQuad };

        animation.Apply(-0.5);

        Assert.Equal(1.0, circle.Opacity);
    }

    [Theory]
    [InlineData(-2.0)]
    [InlineData(-0.5)]
    [InlineData(0.0)]
    public void DrawAnimationIsNeverPartlyDrawnAtOrBeforeItsStart(double t)
    {
        var circle = new VCircle(0, 0, 10);
        var animation = new DrawAnimation(circle, 1.0) { EasingFunction = EasingFunctions.EaseInOutQuad };

        animation.Apply(t);

        Assert.Equal(0, circle.DrawFactor);
    }

    [Fact]
    public void PastTheEndTheEffectIsStillFullyApplied()
    {
        // The other end of the clamp: overshooting must not undo the animation.
        var circle = new VCircle(0, 0, 10);
        var animation = new DrawAnimation(circle, 1.0) { EasingFunction = EasingFunctions.EaseInQuad };

        animation.Apply(1.7);

        Assert.Equal(1.0, circle.DrawFactor);
    }
}
