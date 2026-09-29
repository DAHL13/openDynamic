namespace OpenDynamic.Core.Animation;

/// <summary>
/// Simulates a 1D damped harmonic oscillator (spring physics) with sub-stepping for numerical stability.
/// Preserves velocity when targets change dynamically, avoiding visual snapping and harsh transitions.
/// </summary>
public sealed class Spring
{
    private const double DefaultSubStep = 1.0 / 240.0;
    private const double DefaultSettledThreshold = 0.05;

    public double Value { get; set; }
    public double Velocity { get; set; }
    public double Target { get; set; }

    /// <summary>
    /// Spring stiffness (tension / k). Higher values pull faster towards the target.
    /// Default: 320.
    /// </summary>
    public double Stiffness { get; set; } = 320.0;

    /// <summary>
    /// Damping coefficient (friction / c). Dampens oscillation.
    /// Default: 26.0 (zeta ~0.73: light, elegant bounce).
    /// </summary>
    public double Damping { get; set; } = 26.0;

    /// <summary>
    /// Mass of the oscillating object. Default: 1.0.
    /// </summary>
    public double Mass { get; set; } = 1.0;

    /// <summary>
    /// Maximum threshold for both position error and velocity to consider the spring settled.
    /// Default: 0.05.
    /// </summary>
    public double SettledThreshold { get; set; } = DefaultSettledThreshold;

    /// <summary>
    /// Indicates whether the spring has reached the target and stopped oscillating.
    /// </summary>
    public bool IsSettled =>
        Math.Abs(Target - Value) < SettledThreshold &&
        Math.Abs(Velocity) < SettledThreshold;

    public Spring()
    {
    }

    public Spring(double initialValue, double stiffness = 320.0, double damping = 26.0, double mass = 1.0)
    {
        Value = initialValue;
        Target = initialValue;
        Velocity = 0.0;
        Stiffness = stiffness;
        Damping = damping;
        Mass = mass;
    }

    /// <summary>
    /// Instantly teleports the spring value and target to the specified value, resetting velocity to zero.
    /// </summary>
    public void SnapTo(double value)
    {
        Value = value;
        Target = value;
        Velocity = 0.0;
    }

    /// <summary>
    /// Advances the spring simulation by the given delta time (dt) in seconds.
    /// Uses sub-stepping (symplectic Euler) to maintain numerical stability during variable or large dt.
    /// </summary>
    /// <param name="dt">Time step in seconds.</param>
    public void Step(double dt)
    {
        if (dt <= 0.0)
        {
            return;
        }

        if (IsSettled)
        {
            Value = Target;
            Velocity = 0.0;
            return;
        }

        // Sub-stepping to prevent divergence when dt is large
        int n = Math.Max(1, (int)Math.Ceiling(dt / DefaultSubStep));
        double s = dt / n;
        double mass = Mass <= 0.0 ? 1.0 : Mass;

        for (int i = 0; i < n; i++)
        {
            double force = -Stiffness * (Value - Target) - Damping * Velocity;
            double acceleration = force / mass;

            // Symplectic Euler integration
            Velocity += acceleration * s;
            Value += Velocity * s;
        }

        // If threshold reached at the end of the step, lock to target to avoid residual drift
        if (IsSettled)
        {
            Value = Target;
            Velocity = 0.0;
        }
    }
}
