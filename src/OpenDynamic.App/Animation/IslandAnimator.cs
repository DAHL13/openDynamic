using System.Diagnostics;
using System.Windows.Media;
using OpenDynamic.Core.Animation;
using OpenDynamic.Core.State;
using Serilog;

namespace OpenDynamic.App.Animation;

/// <summary>
/// Orchestrates spring animations for capsule dimensions (Width, Height, CornerRadius, Opacity).
/// Subscribes to <see cref="CompositionTarget.Rendering"/> ONLY during active motion,
/// unsubscribing immediately upon settling to guarantee ~0% CPU usage in idle (Golden Rule 1).
/// </summary>
public sealed class IslandAnimator : IDisposable
{
    private const double MaxDeltaTimeSeconds = 0.050; // Cap dt to 50ms to avoid large jumps

    private long _lastTimestamp;
    private bool _isSubscribed;
    private bool _disposed;

    public Spring WidthSpring { get; }
    public Spring HeightSpring { get; }
    public Spring CornerRadiusSpring { get; }
    public Spring OpacitySpring { get; }

    public IslandStateMachine StateMachine { get; }
    public IslandLayout Layout { get; }

    /// <summary>
    /// Indicates whether the animator is actively subscribed to the WPF composition rendering loop.
    /// In idle/settled state, this is strictly false.
    /// </summary>
    public bool IsSubscribed => _isSubscribed;

    /// <summary>
    /// Indicates whether all 4 springs have settled at their targets.
    /// </summary>
    public bool IsSettled =>
        WidthSpring.IsSettled &&
        HeightSpring.IsSettled &&
        CornerRadiusSpring.IsSettled &&
        OpacitySpring.IsSettled;

    public CapsuleDimensions CurrentDimensions => new(
        Width: WidthSpring.Value,
        Height: HeightSpring.Value,
        CornerRadius: CornerRadiusSpring.Value,
        Opacity: OpacitySpring.Value);

    public CapsuleDimensions TargetDimensions => new(
        Width: WidthSpring.Target,
        Height: HeightSpring.Target,
        CornerRadius: CornerRadiusSpring.Target,
        Opacity: OpacitySpring.Target);

    public event EventHandler? FrameUpdated;
    public event EventHandler? Settled;
    public event EventHandler? Started;

    public IslandAnimator(IslandStateMachine? stateMachine = null, IslandLayout? layout = null)
    {
        StateMachine = stateMachine ?? new IslandStateMachine(IslandState.Compact);
        Layout = layout ?? new IslandLayout();

        var initialDimensions = Layout.GetDimensions(StateMachine.CurrentState);
        WidthSpring = new Spring(initialDimensions.Width);
        HeightSpring = new Spring(initialDimensions.Height);
        CornerRadiusSpring = new Spring(initialDimensions.CornerRadius);
        OpacitySpring = new Spring(initialDimensions.Opacity) { SettledThreshold = 0.005 };
    }

    /// <summary>
    /// Triggers an animated transition towards the specified state if permitted by the state machine.
    /// Velocity is preserved across state switches.
    /// </summary>
    public bool AnimateTo(IslandState targetState)
    {
        if (!StateMachine.CanTransitionTo(targetState))
        {
            Log.Warning("IslandAnimator: Transition from {CurrentState} to {TargetState} was rejected by state machine.",
                StateMachine.CurrentState, targetState);
            return false;
        }

        StateMachine.TryTransitionTo(targetState);
        var dimensions = Layout.GetDimensions(targetState);
        AnimateTo(dimensions);
        return true;
    }

    /// <summary>
    /// Animates the capsule springs to custom target dimensions without modifying the state machine.
    /// Preserves existing velocities.
    /// </summary>
    public void AnimateTo(CapsuleDimensions target)
    {
        WidthSpring.Target = target.Width;
        HeightSpring.Target = target.Height;
        CornerRadiusSpring.Target = target.CornerRadius;
        OpacitySpring.Target = target.Opacity;

        if (!IsSettled)
        {
            SubscribeRendering();
        }
        else
        {
            UnsubscribeRendering();
        }
    }

    /// <summary>
    /// Instantly teleports the capsule springs to the specified state without animation.
    /// </summary>
    public void SnapTo(IslandState targetState)
    {
        StateMachine.TryTransitionTo(targetState);
        var dimensions = Layout.GetDimensions(targetState);
        SnapTo(dimensions);
    }

    /// <summary>
    /// Instantly teleports the capsule springs to the given dimensions and halts motion.
    /// </summary>
    public void SnapTo(CapsuleDimensions dimensions)
    {
        WidthSpring.SnapTo(dimensions.Width);
        HeightSpring.SnapTo(dimensions.Height);
        CornerRadiusSpring.SnapTo(dimensions.CornerRadius);
        OpacitySpring.SnapTo(dimensions.Opacity);

        UnsubscribeRendering();
        FrameUpdated?.Invoke(this, EventArgs.Empty);
        Settled?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Configures stiffness, damping, and mass across all capsule springs.
    /// </summary>
    public void SetSpringParameters(double stiffness, double damping, double mass = 1.0)
    {
        WidthSpring.Stiffness = stiffness;
        WidthSpring.Damping = damping;
        WidthSpring.Mass = mass;

        HeightSpring.Stiffness = stiffness;
        HeightSpring.Damping = damping;
        HeightSpring.Mass = mass;

        CornerRadiusSpring.Stiffness = stiffness;
        CornerRadiusSpring.Damping = damping;
        CornerRadiusSpring.Mass = mass;

        OpacitySpring.Stiffness = stiffness;
        OpacitySpring.Damping = damping;
        OpacitySpring.Mass = mass;
    }

    /// <summary>
    /// Manually steps the spring simulation (useful for deterministic stepping or testing).
    /// </summary>
    public void Step(double dt)
    {
        if (dt <= 0.0) return;

        double clampedDt = Math.Min(dt, MaxDeltaTimeSeconds);

        WidthSpring.Step(clampedDt);
        HeightSpring.Step(clampedDt);
        CornerRadiusSpring.Step(clampedDt);
        OpacitySpring.Step(clampedDt);

        FrameUpdated?.Invoke(this, EventArgs.Empty);

        if (IsSettled)
        {
            UnsubscribeRendering();
            Settled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        long now = Stopwatch.GetTimestamp();
        if (_lastTimestamp == 0)
        {
            _lastTimestamp = now;
            return;
        }

        double dt = (double)(now - _lastTimestamp) / Stopwatch.Frequency;
        _lastTimestamp = now;

        if (dt <= 0.0)
        {
            return;
        }

        double clampedDt = Math.Min(dt, MaxDeltaTimeSeconds);

        WidthSpring.Step(clampedDt);
        HeightSpring.Step(clampedDt);
        CornerRadiusSpring.Step(clampedDt);
        OpacitySpring.Step(clampedDt);

        FrameUpdated?.Invoke(this, EventArgs.Empty);

        if (IsSettled)
        {
            UnsubscribeRendering();
            Settled?.Invoke(this, EventArgs.Empty);
        }
    }

    private void SubscribeRendering()
    {
        if (_isSubscribed || _disposed) return;

        _lastTimestamp = Stopwatch.GetTimestamp();
        CompositionTarget.Rendering += OnRendering;
        _isSubscribed = true;

        Log.Debug("IslandAnimator: Subscribed to CompositionTarget.Rendering. Animation in flight.");
        Started?.Invoke(this, EventArgs.Empty);
    }

    private void UnsubscribeRendering()
    {
        if (!_isSubscribed) return;

        CompositionTarget.Rendering -= OnRendering;
        _isSubscribed = false;
        _lastTimestamp = 0;

        Log.Debug("IslandAnimator: Unsubscribed from CompositionTarget.Rendering (Settled, CPU ~0%).");
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        UnsubscribeRendering();
    }
}
