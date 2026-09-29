namespace OpenDynamic.Core.State;

/// <summary>
/// Finite State Machine enforcing strict transition rules between Dynamic Island states.
/// Rule: Direct transition from Hidden to Expanded is strictly forbidden (must pass through Compact).
/// </summary>
public sealed class IslandStateMachine
{
    public IslandState CurrentState { get; private set; }

    /// <summary>
    /// Event triggered when a valid state transition completes.
    /// </summary>
    public event EventHandler<IslandStateChangedEventArgs>? StateChanged;

    public IslandStateMachine(IslandState initialState = IslandState.Compact)
    {
        CurrentState = initialState;
    }

    /// <summary>
    /// Evaluates whether a transition from the current state to the specified target state is permissible.
    /// </summary>
    public bool CanTransitionTo(IslandState targetState)
    {
        if (targetState == CurrentState)
        {
            return true;
        }

        return CurrentState switch
        {
            IslandState.Hidden => targetState == IslandState.Compact,
            IslandState.Compact => targetState is IslandState.Hidden or IslandState.Split or IslandState.Expanded,
            IslandState.Split => targetState is IslandState.Compact or IslandState.Expanded or IslandState.Hidden,
            IslandState.Expanded => targetState is IslandState.Compact or IslandState.Split or IslandState.Hidden,
            _ => false
        };
    }

    /// <summary>
    /// Attempts to transition to the specified target state.
    /// Returns true if the transition succeeded or if the machine was already in that state; false otherwise.
    /// </summary>
    public bool TryTransitionTo(IslandState targetState)
    {
        if (targetState == CurrentState)
        {
            return true;
        }

        if (!CanTransitionTo(targetState))
        {
            return false;
        }

        var previousState = CurrentState;
        CurrentState = targetState;
        StateChanged?.Invoke(this, new IslandStateChangedEventArgs(previousState, targetState));
        return true;
    }

    /// <summary>
    /// Transitions to the target state or throws an <see cref="InvalidOperationException"/> if invalid.
    /// </summary>
    public void TransitionTo(IslandState targetState)
    {
        if (!TryTransitionTo(targetState))
        {
            throw new InvalidOperationException(
                $"Invalid state transition from '{CurrentState}' to '{targetState}'. Direct transition from Hidden to Expanded or Split is forbidden; capsule must pass through Compact.");
        }
    }
}
