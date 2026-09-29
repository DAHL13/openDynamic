using OpenDynamic.Core.Animation;
using Xunit;

namespace OpenDynamic.Tests.Animation;

public class MotionProfileTests
{
    [Fact]
    public void MotionProfile_Full_HasExpectedValues()
    {
        var profile = MotionProfile.Full;

        Assert.Equal(320.0, profile.Stiffness);
        Assert.Equal(26.0, profile.Damping);
        Assert.Equal(1.0, profile.Mass);
        Assert.Equal(70, profile.CrossFadeOutDurationMs);
        Assert.Equal(90, profile.CrossFadeInDurationMs);
        Assert.True(profile.AllowDecorative);
    }

    [Fact]
    public void MotionProfile_Reduced_HasExpectedValues()
    {
        var profile = MotionProfile.Reduced;

        Assert.Equal(400.0, profile.Stiffness);
        Assert.Equal(40.0, profile.Damping);
        Assert.Equal(1.0, profile.Mass);
        Assert.Equal(40, profile.CrossFadeOutDurationMs);
        Assert.Equal(50, profile.CrossFadeInDurationMs);
        Assert.False(profile.AllowDecorative);
    }

    [Theory]
    [InlineData(MotionMode.Reduced, true, false)]
    [InlineData(MotionMode.Reduced, false, false)]
    [InlineData(MotionMode.Full, true, true)]
    [InlineData(MotionMode.Full, false, true)]
    [InlineData(MotionMode.Auto, true, true)]
    [InlineData(MotionMode.Auto, false, false)]
    public void MotionProfileResolver_Resolve_ResolvesCorrectProfile(
        MotionMode mode,
        bool systemAnimationsEnabled,
        bool expectedAllowDecorative)
    {
        var profile = MotionProfileResolver.Resolve(mode, systemAnimationsEnabled);

        Assert.NotNull(profile);
        Assert.Equal(expectedAllowDecorative, profile.AllowDecorative);

        if (expectedAllowDecorative)
        {
            Assert.Equal(MotionProfile.Full, profile);
        }
        else
        {
            Assert.Equal(MotionProfile.Reduced, profile);
        }
    }

    [Fact]
    public void Spring_WithFullProfile_ProducesOvershoot()
    {
        // Full profile uses underdamped spring (zeta ~0.73) which exhibits natural overshoot bounce
        var profile = MotionProfile.Full;
        var spring = new Spring(0.0, profile.Stiffness, profile.Damping, profile.Mass)
        {
            Target = 100.0
        };

        bool hasOvershoot = false;
        double maxOvershoot = 0.0;
        const double dt = 1.0 / 120.0;

        for (int i = 0; i < 240 && !spring.IsSettled; i++)
        {
            spring.Step(dt);
            if (spring.Value > 100.0)
            {
                hasOvershoot = true;
                maxOvershoot = Math.Max(maxOvershoot, spring.Value - 100.0);
            }
        }

        Assert.True(hasOvershoot, "Underdamped Full profile spring must produce an overshoot bounce above 100.0.");
        Assert.True(maxOvershoot > 1.0, $"Expected measurable overshoot, got {maxOvershoot}.");
        Assert.True(spring.IsSettled);
        Assert.Equal(100.0, spring.Value);
    }

    [Theory]
    [InlineData(0.0, 100.0)]
    [InlineData(100.0, 0.0)]
    [InlineData(200.0, 400.0)]
    [InlineData(400.0, 200.0)]
    public void Spring_WithReducedProfile_NeverOvershootsTarget(double initial, double target)
    {
        // Reduced profile is critically damped (zeta = 1.0), meaning it strictly converges without overshoot
        var profile = MotionProfile.Reduced;
        var spring = new Spring(initial, profile.Stiffness, profile.Damping, profile.Mass)
        {
            Target = target
        };

        const double dt = 1.0 / 120.0;
        const double epsilon = 0.0001;
        bool isIncreasing = target > initial;

        for (int i = 0; i < 300 && !spring.IsSettled; i++)
        {
            spring.Step(dt);

            if (isIncreasing)
            {
                Assert.True(spring.Value <= target + epsilon,
                    $"Spring value {spring.Value} exceeded target {target} in Reduced mode at frame {i}.");
            }
            else
            {
                Assert.True(spring.Value >= target - epsilon,
                    $"Spring value {spring.Value} went below target {target} in Reduced mode at frame {i}.");
            }
        }

        Assert.True(spring.IsSettled);
        Assert.Equal(target, spring.Value, precision: 3);
    }
}
