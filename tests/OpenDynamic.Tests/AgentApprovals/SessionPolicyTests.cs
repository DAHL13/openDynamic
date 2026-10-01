using OpenDynamic.Core.AgentApprovals;
using OpenDynamic.Tests.Timer;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class SessionPolicyTests
{
    [Fact]
    public void SessionPolicy_EnforcesAccidentalClickGracePeriod()
    {
        var startTime = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);

        var request = new ApprovalRequest(
            id: "req-1",
            conversationId: "conv",
            stepIdx: 1,
            toolName: "run_command",
            commandLine: "git status");

        var policy = new ApprovalSessionPolicy(request, clock);

        // Within 0 ms: in grace period
        Assert.True(policy.IsInGracePeriod());
        Assert.False(policy.CanAllow());
        Assert.False(policy.CanDeny());
        Assert.False(policy.TryAllow(out var res1));

        // Advance 300 ms: still in grace period
        clock.Advance(TimeSpan.FromMilliseconds(300));
        Assert.True(policy.IsInGracePeriod());
        Assert.False(policy.CanAllow());
        Assert.False(policy.CanDeny());

        // Advance 301 ms (total 601 ms): grace period elapsed
        clock.Advance(TimeSpan.FromMilliseconds(301));
        Assert.False(policy.IsInGracePeriod());
        Assert.True(policy.CanAllow());
        Assert.True(policy.CanDeny());
        Assert.True(policy.TryAllow(out var res2));
        Assert.Equal("allow", res2.Decision);
    }

    [Fact]
    public void SessionPolicy_RequiresExpandedReview_ForTruncatedOrHighRisk()
    {
        var startTime = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);

        var highRiskRequest = new ApprovalRequest(
            id: "req-high",
            conversationId: "conv",
            stepIdx: 1,
            toolName: "run_command",
            commandLine: "rm -rf /");

        var policy = new ApprovalSessionPolicy(highRiskRequest, clock);
        clock.Advance(TimeSpan.FromMilliseconds(700));

        // High risk requires expanded review before allowing
        Assert.False(policy.IsInGracePeriod());
        Assert.False(policy.CanAllow());
        Assert.False(policy.CanAllowViaHotkey());
        Assert.True(policy.CanDeny()); // Deny is always allowed after grace period

        // Expand the review
        policy.MarkExpanded();
        Assert.True(policy.CanAllow());
        // BUT hotkey approval remains strictly blocked for High risk!
        Assert.False(policy.CanAllowViaHotkey());

        Assert.True(policy.TryAllow(out var allowRes));
        Assert.Equal("allow", allowRes.Decision);
    }

    [Fact]
    public void SessionPolicy_Enforces90SecondTimeout_FallingBackToAskNative()
    {
        var startTime = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);

        var request = new ApprovalRequest(
            id: "req-timeout",
            conversationId: "conv",
            stepIdx: 1,
            toolName: "run_command",
            commandLine: "npm test");

        var policy = new ApprovalSessionPolicy(request, clock);
        clock.Advance(TimeSpan.FromSeconds(89));
        Assert.False(policy.HasTimedOut());

        clock.Advance(TimeSpan.FromSeconds(2)); // Total 91 seconds
        Assert.True(policy.HasTimedOut());

        var expResponse = policy.Expire();
        Assert.Equal("ask", expResponse.Decision);
        Assert.Contains("Tiempo de espera agotado", expResponse.Reason);
        Assert.True(policy.IsResolved);
    }

    [Fact]
    public void SessionPolicy_ClientDisconnect_FallsBackSafelyToAskNative()
    {
        var clock = new FakeTimeProvider();
        var request = new ApprovalRequest("id", "c", 1, "run_command", "npm test");
        var policy = new ApprovalSessionPolicy(request, clock);

        var disconnectResponse = policy.CancelByClientDisconnect();
        Assert.Equal("ask", disconnectResponse.Decision);
        Assert.True(policy.IsResolved);
    }

    [Fact]
    public void SessionPolicy_SystemUnavailable_FallsBackSafelyToAskNative()
    {
        var clock = new FakeTimeProvider();
        var request = new ApprovalRequest("id", "c", 1, "run_command", "npm test");
        var policy = new ApprovalSessionPolicy(request, clock);

        var response = policy.RejectBySystemUnavailable("Pantalla completa exclusiva activa");
        Assert.Equal("ask", response.Decision);
        Assert.Contains("Pantalla completa", response.Reason);
        Assert.True(policy.IsResolved);
    }

    [Fact]
    public void SessionPolicy_DenyWithReason_SetsResolutionCorrectly()
    {
        var clock = new FakeTimeProvider();
        var request = new ApprovalRequest("id", "c", 1, "run_command", "npm test");
        var policy = new ApprovalSessionPolicy(request, clock);
        clock.Advance(TimeSpan.FromMilliseconds(700));

        Assert.True(policy.TryDeny("No: usa otro enfoque", out var response));
        Assert.Equal("deny", response.Decision);
        Assert.Equal("No: usa otro enfoque", response.Reason);
        Assert.True(policy.IsResolved);
    }
}
