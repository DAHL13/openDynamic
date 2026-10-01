using OpenDynamic.Core.AgentApprovals;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class ApprovalRuleMatcherTests
{
    private static ApprovalRequest CreateRunRequest(
        string command,
        string convId = "conv-001",
        string? cwd = @"C:\repos\projectA",
        IReadOnlyList<string>? workspacePaths = null)
    {
        return new ApprovalRequest(
            id: "req-1",
            conversationId: convId,
            stepIdx: 1,
            toolName: "run_command",
            commandLine: command,
            cwd: cwd,
            workspacePaths: workspacePaths ?? [@"C:\repos\projectA"]);
    }

    [Fact]
    public void ExactMatch_IdenticalCommand_Matches()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Global,
            "git status");

        var request = CreateRunRequest("git status");

        Assert.True(ApprovalRuleMatcher.Matches(rule, request, RiskLevel.Low));
    }

    [Fact]
    public void ExactMatch_CasingDifferences_DoNotMatch()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Global,
            "git status");

        var request = CreateRunRequest("Git Status");

        Assert.False(ApprovalRuleMatcher.Matches(rule, request, RiskLevel.Low));
    }

    [Fact]
    public void ExactMatch_InternalSpaces_PreservedAndDoNotMatch()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Global,
            "npm test");

        var request = CreateRunRequest("npm  test");

        Assert.False(ApprovalRuleMatcher.Matches(rule, request, RiskLevel.Low));
    }

    [Fact]
    public void ExactMatch_TrailingAndLeadingWhitespace_Normalized()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Global,
            "dotnet build");

        var request = CreateRunRequest("   dotnet build   \n");

        Assert.True(ApprovalRuleMatcher.Matches(rule, request, RiskLevel.Low));
    }

    [Fact]
    public void ExactMatch_LineBreaks_Normalized()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Global,
            "echo hello\necho world");

        var request = CreateRunRequest("echo hello\r\necho world");

        Assert.True(ApprovalRuleMatcher.Matches(rule, request, RiskLevel.Low));
    }

    [Fact]
    public void ExactMatch_CompoundCommands_MatchesIdentical()
    {
        string compound = "git status; git log -n 2 --oneline";
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Conversation,
            compound,
            conversationId: "conv-compound");

        var req1 = CreateRunRequest(compound, convId: "conv-compound");
        var req2 = CreateRunRequest("git status", convId: "conv-compound");

        Assert.True(ApprovalRuleMatcher.Matches(rule, req1, RiskLevel.Low));
        Assert.False(ApprovalRuleMatcher.Matches(rule, req2, RiskLevel.Low));
    }

    [Fact]
    public void RiskHigh_NeverMatches_EvenWithExactPattern()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Global,
            "rm -rf /");

        var request = CreateRunRequest("rm -rf /");

        // Golden Rule 12: High-risk can never match or be auto-approved
        Assert.False(ApprovalRuleMatcher.Matches(rule, request, RiskLevel.High));
    }

    [Fact]
    public void FileTools_NeverMatch()
    {
        var rule = new ApprovalRule
        {
            Scope = ApprovalRuleScope.Global,
            ToolName = "write_to_file",
            CommandPattern = "test.txt"
        };

        var request = new ApprovalRequest(
            id: "req-file",
            conversationId: "conv-1",
            stepIdx: 1,
            toolName: "write_to_file",
            targetFile: "test.txt",
            codeContent: "hello");

        Assert.False(ApprovalRuleMatcher.Matches(rule, request, RiskLevel.Low));
    }

    [Fact]
    public void Scope_Conversation_MatchesOnlyMatchingConversationId()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Conversation,
            "npm test",
            conversationId: "conv-target");

        var matchReq = CreateRunRequest("npm test", convId: "conv-target");
        var otherReq = CreateRunRequest("npm test", convId: "conv-other");

        Assert.True(ApprovalRuleMatcher.Matches(rule, matchReq, RiskLevel.Low));
        Assert.False(ApprovalRuleMatcher.Matches(rule, otherReq, RiskLevel.Low));
    }

    [Fact]
    public void Scope_Project_MatchesWorkspacePath()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Project,
            "dotnet test",
            workspaceKey: @"C:\repos\myProject");

        var matchReq = CreateRunRequest("dotnet test", workspacePaths: [@"C:\repos\myProject\"]);
        var otherReq = CreateRunRequest("dotnet test", workspacePaths: [@"C:\repos\anotherProject"]);

        Assert.True(ApprovalRuleMatcher.Matches(rule, matchReq, RiskLevel.Low));
        Assert.False(ApprovalRuleMatcher.Matches(rule, otherReq, RiskLevel.Low));
    }

    [Fact]
    public void Scope_Project_MatchesCwd_WhenWorkspacePathsEmpty()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Project,
            "cargo build",
            workspaceKey: @"C:\rust\app");

        var matchReq = CreateRunRequest("cargo build", cwd: @"c:\rust\app", workspacePaths: []);
        var otherReq = CreateRunRequest("cargo build", cwd: @"c:\rust\other", workspacePaths: []);

        Assert.True(ApprovalRuleMatcher.Matches(rule, matchReq, RiskLevel.Low));
        Assert.False(ApprovalRuleMatcher.Matches(rule, otherReq, RiskLevel.Low));
    }

    [Fact]
    public void Scope_Global_MatchesAnyProjectOrConversation()
    {
        var rule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Global,
            "git status");

        var req1 = CreateRunRequest("git status", convId: "c1", cwd: @"D:\a");
        var req2 = CreateRunRequest("git status", convId: "c2", cwd: @"E:\b");

        Assert.True(ApprovalRuleMatcher.Matches(rule, req1, RiskLevel.Low));
        Assert.True(ApprovalRuleMatcher.Matches(rule, req2, RiskLevel.Low));
    }

    [Fact]
    public void PrefixMatch_InGlobalOrConversationScope_NeverMatches()
    {
        var ruleGlobalPrefix = new ApprovalRule
        {
            Scope = ApprovalRuleScope.Global,
            ToolName = "run_command",
            CommandPattern = "git log",
            IsPrefixMatch = true
        };

        var ruleConvPrefix = new ApprovalRule
        {
            Scope = ApprovalRuleScope.Conversation,
            ConversationId = "conv-1",
            ToolName = "run_command",
            CommandPattern = "git log",
            IsPrefixMatch = true
        };

        var request = CreateRunRequest("git log --oneline", convId: "conv-1");

        // Safe prefix rules are only valid for Project scope
        Assert.False(ApprovalRuleMatcher.Matches(ruleGlobalPrefix, request, RiskLevel.Low));
        Assert.False(ApprovalRuleMatcher.Matches(ruleConvPrefix, request, RiskLevel.Low));
    }
}
