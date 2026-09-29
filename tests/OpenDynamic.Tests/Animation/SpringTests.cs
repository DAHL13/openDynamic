using OpenDynamic.Core.Animation;

namespace OpenDynamic.Tests.Animation;

public class SpringTests
{
    [Fact]
    public void Spring_InitialState_SettledWhenValueEqualsTarget()
    {
        var spring = new Spring(100.0);

        Assert.Equal(100.0, spring.Value);
        Assert.Equal(100.0, spring.Target);
        Assert.Equal(0.0, spring.Velocity);
        Assert.True(spring.IsSettled);
    }

    [Fact]
    public void SnapTo_InstantlyUpdatesValueAndTargetAndResetsVelocity()
    {
        var spring = new Spring(0.0)
        {
            Target = 100.0,
            Velocity = 45.0
        };

        spring.SnapTo(250.0);

        Assert.Equal(250.0, spring.Value);
        Assert.Equal(250.0, spring.Target);
        Assert.Equal(0.0, spring.Velocity);
        Assert.True(spring.IsSettled);
    }

    [Theory]
    [InlineData(0.0, 100.0)]
    [InlineData(400.0, 160.0)]
    [InlineData(-50.0, 50.0)]
    public void Spring_ConvergencesToTargetAndSettles(double start, double target)
    {
        var spring = new Spring(start)
        {
            Target = target
        };

        Assert.False(spring.IsSettled);

        // Simulate up to 3 seconds at 60 fps (dt = 1/60)
        const double dt = 1.0 / 60.0;
        int maxFrames = 180;
        int frame = 0;

        while (!spring.IsSettled && frame < maxFrames)
        {
            spring.Step(dt);
            frame++;
        }

        Assert.True(spring.IsSettled, $"Spring did not settle within {maxFrames} frames. Current value: {spring.Value}, Velocity: {spring.Velocity}");
        Assert.Equal(target, spring.Value, precision: 4);
        Assert.Equal(0.0, spring.Velocity);
    }

    [Fact]
    public void Spring_PreservesVelocityWhenTargetChangesDynamically()
    {
        var spring = new Spring(0.0)
        {
            Target = 100.0
        };

        // Advance simulation slightly to build momentum
        for (int i = 0; i < 5; i++)
        {
            spring.Step(1.0 / 60.0);
        }

        double velocityBefore = spring.Velocity;
        Assert.True(velocityBefore > 0.0, "Spring should have positive velocity towards target.");

        // Dynamically change target while in motion
        spring.Target = 300.0;
        double velocityAfter = spring.Velocity;

        Assert.Equal(velocityBefore, velocityAfter);

        // Further steps continue from existing velocity and eventually settle to new target
        for (int i = 0; i < 200; i++)
        {
            spring.Step(1.0 / 60.0);
        }

        Assert.True(spring.IsSettled);
        Assert.Equal(300.0, spring.Value);
    }

    [Theory]
    [InlineData(0.05)]
    [InlineData(0.1)]
    [InlineData(0.5)]
    [InlineData(1.0)]
    public void Spring_RemainsNumericallyStableUnderLargeDtSpikes(double largeDt)
    {
        var spring = new Spring(0.0)
        {
            Target = 100.0
        };

        // Take a single step with large dt
        spring.Step(largeDt);

        Assert.False(double.IsNaN(spring.Value), "Value should not be NaN");
        Assert.False(double.IsInfinity(spring.Value), "Value should not be Infinity");
        Assert.False(double.IsNaN(spring.Velocity), "Velocity should not be NaN");
        Assert.False(double.IsInfinity(spring.Velocity), "Velocity should not be Infinity");

        // Continue running with normal steps to ensure it converges
        for (int i = 0; i < 180; i++)
        {
            spring.Step(1.0 / 60.0);
        }

        Assert.True(spring.IsSettled);
        Assert.Equal(100.0, spring.Value);
    }

    [Fact]
    public void Spring_HandlesJitteryAndVariableDtWithoutDiverging()
    {
        var spring = new Spring(0.0)
        {
            Target = 160.0
        };

        var random = new Random(42);
        double totalTime = 0.0;

        while (totalTime < 2.5 && !spring.IsSettled)
        {
            // Random dt between 1ms and 80ms simulating frame drops
            double variableDt = random.NextDouble() * 0.079 + 0.001;
            spring.Step(variableDt);
            totalTime += variableDt;

            Assert.False(double.IsNaN(spring.Value));
            Assert.False(double.IsInfinity(spring.Value));
        }

        Assert.True(spring.IsSettled);
        Assert.Equal(160.0, spring.Value);
    }

    [Fact]
    public void Spring_ExhibitsSlightUnderdampedOvershoot()
    {
        // With default Stiffness = 320, Damping = 26, Mass = 1 (zeta ~0.73),
        // there should be a small overshoot before settling.
        var spring = new Spring(0.0)
        {
            Target = 100.0
        };

        double maxValue = 0.0;
        for (int i = 0; i < 120; i++)
        {
            spring.Step(1.0 / 120.0);
            if (spring.Value > maxValue)
            {
                maxValue = spring.Value;
            }
        }

        Assert.True(maxValue > 100.0, $"Expected slight overshoot above 100, but max was {maxValue}");
        Assert.True(maxValue < 110.0, $"Overshoot should be subtle (below 110), but was {maxValue}");
    }

    [Fact]
    public void Spring_NonPositiveDt_DoesNotAdvanceSimulation()
    {
        var spring = new Spring(10.0)
        {
            Target = 100.0,
            Velocity = 5.0
        };

        spring.Step(0.0);
        Assert.Equal(10.0, spring.Value);
        Assert.Equal(5.0, spring.Velocity);

        spring.Step(-0.016);
        Assert.Equal(10.0, spring.Value);
        Assert.Equal(5.0, spring.Velocity);
    }

    [Fact]
    public void Spring_ZeroOrNegativeMass_DefaultsToSafeMassWithoutDivisionByZero()
    {
        var spring = new Spring(0.0)
        {
            Target = 100.0,
            Mass = 0.0
        };

        spring.Step(0.016);

        Assert.False(double.IsNaN(spring.Value));
        Assert.False(double.IsInfinity(spring.Value));
    }
}
