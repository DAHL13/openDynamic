using OpenDynamic.Core.AgentApprovals;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class ApprovalHistoryTrackerTests
{
    [Fact]
    public void Record_InsertsAtBeginning_LatestFirst()
    {
        var tracker = new ApprovalHistoryTracker();

        tracker.Record("run_command", "allow", "git status", "projectA");
        tracker.Record("run_command", "deny", "rm -rf /", "projectA");

        var recent = tracker.GetRecent();
        Assert.Equal(2, recent.Count);
        Assert.Equal("rm -rf /", recent[0].Summary);
        Assert.Equal("git status", recent[1].Summary);
    }

    [Fact]
    public void Record_CapsAt50Items_CircularBuffer()
    {
        var tracker = new ApprovalHistoryTracker();

        for (int i = 1; i <= 60; i++)
        {
            tracker.Record("run_command", "allow", $"command-{i}", "proj");
        }

        var recent = tracker.GetRecent();
        Assert.Equal(50, recent.Count);
        Assert.Equal("command-60", recent[0].Summary);
        Assert.Equal("command-11", recent[49].Summary);
    }

    [Fact]
    public void Record_TruncatesSummaryTo60Chars_WithEllipsis()
    {
        var tracker = new ApprovalHistoryTracker();

        string veryLongCommand = "dotnet build --configuration Release --verbosity detailed --property:WarningLevel=99 --framework net10.0";
        Assert.True(veryLongCommand.Length > 60);

        tracker.Record("run_command", "allow", veryLongCommand, "proj");

        var item = tracker.GetRecent()[0];
        Assert.True(item.Summary.Length <= 60);
        Assert.EndsWith("...", item.Summary);
    }

    [Fact]
    public void Record_SanitizesFileToolPathsToFileName_ZeroPathLeakage()
    {
        var tracker = new ApprovalHistoryTracker();

        var request = new ApprovalRequest(
            id: "req-file",
            conversationId: "c1",
            stepIdx: 1,
            toolName: "write_to_file",
            targetFile: @"C:\Users\Pcrz\SecretWorkspace\SensitiveSubfolder\super_secret.cs",
            codeContent: "secret content",
            workspacePaths: [@"C:\Users\Pcrz\SecretWorkspace"]);

        tracker.Record(request, "allow", "User approved");

        var item = tracker.GetRecent()[0];
        // Must contain only the filename, not full path
        Assert.Equal("super_secret.cs", item.Summary);
        Assert.DoesNotContain(@"C:\Users\Pcrz", item.Summary);
        Assert.Equal("SecretWorkspace", item.WorkspaceFolder);
    }

    [Fact]
    public void Clear_EmptiesHistory()
    {
        var tracker = new ApprovalHistoryTracker();
        tracker.Record("run_command", "allow", "git status", "proj");

        Assert.NotEmpty(tracker.GetRecent());

        tracker.Clear();
        Assert.Empty(tracker.GetRecent());
    }
}
