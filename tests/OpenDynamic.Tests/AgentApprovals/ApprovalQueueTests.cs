using OpenDynamic.Core.AgentApprovals;
using OpenDynamic.Tests.Timer;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class ApprovalQueueTests
{
    private sealed record QueuedItem(ApprovalSessionPolicy Session, TaskCompletionSource<ApprovalResponse> Tcs);

    [Fact]
    public void Queue_MaintainsFIFOOrder_AndResolvesSequentially()
    {
        var startTime = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);

        var queue = new List<QueuedItem>();

        var req1 = new ApprovalRequest("req-1", "conv-1", 1, "run_command", "git status");
        var req2 = new ApprovalRequest("req-2", "conv-2", 2, "run_command", "dotnet build");
        var req3 = new ApprovalRequest("req-3", "conv-3", 3, "run_command", "npm test");

        var item1 = new QueuedItem(new ApprovalSessionPolicy(req1, clock), new TaskCompletionSource<ApprovalResponse>());
        var item2 = new QueuedItem(new ApprovalSessionPolicy(req2, clock), new TaskCompletionSource<ApprovalResponse>());
        var item3 = new QueuedItem(new ApprovalSessionPolicy(req3, clock), new TaskCompletionSource<ApprovalResponse>());

        queue.Add(item1);
        queue.Add(item2);
        queue.Add(item3);

        // Position indicators
        Assert.Equal("1 / 3", $"1 / {queue.Count}");

        // Resolve first item
        var active = queue[0];
        Assert.Equal("req-1", active.Session.Request.Id);
        active.Session.TryAllow(out var res1);
        active.Tcs.SetResult(res1);
        queue.RemoveAt(0);

        Assert.Equal("1 / 2", $"1 / {queue.Count}");
        Assert.Equal("req-2", queue[0].Session.Request.Id);

        // Resolve second item
        active = queue[0];
        active.Session.TryAllow(out var res2);
        active.Tcs.SetResult(res2);
        queue.RemoveAt(0);

        Assert.Equal("1 / 1", $"1 / {queue.Count}");
        Assert.Equal("req-3", queue[0].Session.Request.Id);
    }

    [Fact]
    public void Queue_MaintainsIndependentGracePeriods()
    {
        var startTime = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);

        // Session 1 arrives at T=0
        var session1 = new ApprovalSessionPolicy(
            new ApprovalRequest("r1", "c1", 1, "run_command", "cmd1"),
            clock);

        // Advance 400ms
        clock.Advance(TimeSpan.FromMilliseconds(400));

        // Session 2 arrives at T=400ms
        var session2 = new ApprovalSessionPolicy(
            new ApprovalRequest("r2", "c2", 2, "run_command", "cmd2"),
            clock);

        // Advance 250ms (T=650ms)
        clock.Advance(TimeSpan.FromMilliseconds(250));

        // Session 1 has elapsed 650ms (> 600ms grace period) -> Ready!
        Assert.False(session1.IsInGracePeriod());
        Assert.True(session1.CanAllow());

        // Session 2 has only elapsed 250ms (< 600ms grace period) -> Still in grace!
        Assert.True(session2.IsInGracePeriod());
        Assert.False(session2.CanAllow());
    }

    [Fact]
    public void Queue_ClientDisconnect_CancelsSpecificQueuedItemWithoutAffectingOthers()
    {
        var startTime = new DateTimeOffset(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(startTime);

        var queue = new List<QueuedItem>();

        var req1 = new ApprovalRequest("req-1", "conv-1", 1, "run_command", "cmd1");
        var req2 = new ApprovalRequest("req-2", "conv-2", 2, "run_command", "cmd2");
        var req3 = new ApprovalRequest("req-3", "conv-3", 3, "run_command", "cmd3");

        queue.Add(new QueuedItem(new ApprovalSessionPolicy(req1, clock), new TaskCompletionSource<ApprovalResponse>()));
        queue.Add(new QueuedItem(new ApprovalSessionPolicy(req2, clock), new TaskCompletionSource<ApprovalResponse>()));
        queue.Add(new QueuedItem(new ApprovalSessionPolicy(req3, clock), new TaskCompletionSource<ApprovalResponse>()));

        // Client 2 disconnects
        string cancelledId = "req-2";
        int index = queue.FindIndex(q => q.Session.Request.Id == cancelledId);
        Assert.Equal(1, index);

        queue[index].Session.CancelByClientDisconnect();
        queue[index].Tcs.TrySetCanceled();
        queue.RemoveAt(index);

        // Remaining queue should have req-1 and req-3
        Assert.Equal(2, queue.Count);
        Assert.Equal("req-1", queue[0].Session.Request.Id);
        Assert.Equal("req-3", queue[1].Session.Request.Id);
    }
}
