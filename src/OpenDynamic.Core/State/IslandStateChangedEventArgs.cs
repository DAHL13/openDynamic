namespace OpenDynamic.Core.State;

/// <summary>
/// Provides data for the <see cref="IslandStateMachine.StateChanged"/> event.
/// </summary>
public sealed class IslandStateChangedEventArgs : EventArgs
{
    public IslandState PreviousState { get; }
    public IslandState CurrentState { get; }

    public IslandStateChangedEventArgs(IslandState previousState, IslandState currentState)
    {
        PreviousState = previousState;
        CurrentState = currentState;
    }
}
