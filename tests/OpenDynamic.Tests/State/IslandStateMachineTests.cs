using OpenDynamic.Core.State;

namespace OpenDynamic.Tests.State;

public class IslandStateMachineTests
{
    [Fact]
    public void InitialState_DefaultsToCompact()
    {
        var fsm = new IslandStateMachine();
        Assert.Equal(IslandState.Compact, fsm.CurrentState);
    }

    [Fact]
    public void CustomInitialState_IsRespected()
    {
        var fsm = new IslandStateMachine(IslandState.Hidden);
        Assert.Equal(IslandState.Hidden, fsm.CurrentState);
    }

    [Fact]
    public void HiddenToExpanded_IsStrictlyForbidden()
    {
        var fsm = new IslandStateMachine(IslandState.Hidden);

        Assert.False(fsm.CanTransitionTo(IslandState.Expanded));
        Assert.False(fsm.TryTransitionTo(IslandState.Expanded));
        Assert.Equal(IslandState.Hidden, fsm.CurrentState);

        var ex = Assert.Throws<InvalidOperationException>(() => fsm.TransitionTo(IslandState.Expanded));
        Assert.Contains("Hidden", ex.Message);
        Assert.Contains("Expanded", ex.Message);
    }

    [Fact]
    public void HiddenToSplit_IsStrictlyForbidden()
    {
        var fsm = new IslandStateMachine(IslandState.Hidden);

        Assert.False(fsm.CanTransitionTo(IslandState.Split));
        Assert.False(fsm.TryTransitionTo(IslandState.Split));
        Assert.Equal(IslandState.Hidden, fsm.CurrentState);

        Assert.Throws<InvalidOperationException>(() => fsm.TransitionTo(IslandState.Split));
    }

    [Fact]
    public void HiddenToCompact_IsValid()
    {
        var fsm = new IslandStateMachine(IslandState.Hidden);

        Assert.True(fsm.CanTransitionTo(IslandState.Compact));
        Assert.True(fsm.TryTransitionTo(IslandState.Compact));
        Assert.Equal(IslandState.Compact, fsm.CurrentState);
    }

    [Theory]
    [InlineData(IslandState.Hidden)]
    [InlineData(IslandState.Split)]
    [InlineData(IslandState.Expanded)]
    public void Compact_CanTransitionTo_AllOtherStates(IslandState target)
    {
        var fsm = new IslandStateMachine(IslandState.Compact);

        Assert.True(fsm.CanTransitionTo(target));
        Assert.True(fsm.TryTransitionTo(target));
        Assert.Equal(target, fsm.CurrentState);
    }

    [Theory]
    [InlineData(IslandState.Compact)]
    [InlineData(IslandState.Split)]
    [InlineData(IslandState.Hidden)]
    public void Expanded_CanTransitionTo_ValidStates(IslandState target)
    {
        var fsm = new IslandStateMachine(IslandState.Expanded);

        Assert.True(fsm.CanTransitionTo(target));
        Assert.True(fsm.TryTransitionTo(target));
        Assert.Equal(target, fsm.CurrentState);
    }

    [Theory]
    [InlineData(IslandState.Compact)]
    [InlineData(IslandState.Expanded)]
    [InlineData(IslandState.Hidden)]
    public void Split_CanTransitionTo_ValidStates(IslandState target)
    {
        var fsm = new IslandStateMachine(IslandState.Split);

        Assert.True(fsm.CanTransitionTo(target));
        Assert.True(fsm.TryTransitionTo(target));
        Assert.Equal(target, fsm.CurrentState);
    }

    [Fact]
    public void SameStateTransition_ReturnsTrueWithoutFiringEvent()
    {
        var fsm = new IslandStateMachine(IslandState.Compact);
        bool eventFired = false;
        fsm.StateChanged += (_, _) => eventFired = true;

        Assert.True(fsm.CanTransitionTo(IslandState.Compact));
        Assert.True(fsm.TryTransitionTo(IslandState.Compact));
        Assert.False(eventFired);
        Assert.Equal(IslandState.Compact, fsm.CurrentState);
    }

    [Fact]
    public void StateChanged_FiresWithCorrectPreviousAndNewStates()
    {
        var fsm = new IslandStateMachine(IslandState.Compact);
        IslandState? observedPrevious = null;
        IslandState? observedCurrent = null;

        fsm.StateChanged += (_, e) =>
        {
            observedPrevious = e.PreviousState;
            observedCurrent = e.CurrentState;
        };

        fsm.TransitionTo(IslandState.Expanded);

        Assert.Equal(IslandState.Compact, observedPrevious);
        Assert.Equal(IslandState.Expanded, observedCurrent);
        Assert.Equal(IslandState.Expanded, fsm.CurrentState);
    }

    [Fact]
    public void MultiStepTransition_HiddenToExpandedThroughCompact_Succeeds()
    {
        var fsm = new IslandStateMachine(IslandState.Hidden);

        fsm.TransitionTo(IslandState.Compact);
        Assert.Equal(IslandState.Compact, fsm.CurrentState);

        fsm.TransitionTo(IslandState.Expanded);
        Assert.Equal(IslandState.Expanded, fsm.CurrentState);
    }
}
