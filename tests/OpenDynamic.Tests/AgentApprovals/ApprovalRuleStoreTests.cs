using System.IO;
using System.Text.Json;
using OpenDynamic.Core.AgentApprovals;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class ApprovalRuleStoreTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _globalRulesPath;

    public ApprovalRuleStoreTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "od_store_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        _globalRulesPath = Path.Combine(_tempDir, "global-rules.json");
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, recursive: true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }

    [Fact]
    public void ConversationRules_AreRAMOnly_AndDoNotTouchDisk()
    {
        var store = new ApprovalRuleStore(_globalRulesPath);

        var convRule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Conversation,
            "git status",
            conversationId: "conv-101");

        store.AddRule(convRule);

        var loadedRules = store.GetRules(ApprovalRuleScope.Conversation);
        Assert.Single(loadedRules);
        Assert.Equal("git status", loadedRules[0].CommandPattern);

        // Verify no files created for conversation
        Assert.False(File.Exists(_globalRulesPath));
    }

    [Fact]
    public void ProjectRules_PersistedAtomicallyToDisk_WithBackup()
    {
        string projectDir = Path.Combine(_tempDir, "project1");
        Directory.CreateDirectory(projectDir);

        var store = new ApprovalRuleStore(_globalRulesPath);

        var projectRule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Project,
            "dotnet test",
            workspaceKey: projectDir);

        store.AddRule(projectRule, workspaceRoot: projectDir);

        string expectedPath = ApprovalRuleStore.GetProjectRulesFilePath(projectDir);
        Assert.True(File.Exists(expectedPath));

        // Add second rule to trigger backup creation
        var projectRule2 = ApprovalRule.CreateExact(
            ApprovalRuleScope.Project,
            "dotnet build",
            workspaceKey: projectDir);

        store.AddRule(projectRule2, workspaceRoot: projectDir);

        string bakPath = expectedPath + ".bak";
        Assert.True(File.Exists(bakPath));

        // Create new store instance and load project rules
        var newStore = new ApprovalRuleStore(_globalRulesPath);
        newStore.LoadProjectRules(projectDir);

        var reloaded = newStore.GetRules(ApprovalRuleScope.Project);
        Assert.Equal(2, reloaded.Count);
    }

    [Fact]
    public void GlobalRules_PersistedAtomicallyToDisk_WithBackup()
    {
        var store = new ApprovalRuleStore(_globalRulesPath);

        var rule1 = ApprovalRule.CreateExact(ApprovalRuleScope.Global, "git status");
        store.AddRule(rule1);

        Assert.True(File.Exists(_globalRulesPath));

        var rule2 = ApprovalRule.CreateExact(ApprovalRuleScope.Global, "git diff");
        store.AddRule(rule2);

        string bakPath = _globalRulesPath + ".bak";
        Assert.True(File.Exists(bakPath));

        // Reload fresh store
        var newStore = new ApprovalRuleStore(_globalRulesPath);
        var rules = newStore.GetRules(ApprovalRuleScope.Global);
        Assert.Equal(2, rules.Count);
    }

    [Fact]
    public void CorruptedJson_RecoversFromBackupFile()
    {
        // 1. Arrange valid file and valid backup
        string validBackup = """
        {
          "schemaVersion": 1,
          "rules": [
            {
              "id": "rule-recovered",
              "scope": "Global",
              "toolName": "run_command",
              "commandPattern": "git status"
            }
          ]
        }
        """;

        string corruptedFile = "INVALID JSON {{{ NOT CLOSED";

        File.WriteAllText(_globalRulesPath, corruptedFile);
        File.WriteAllText(_globalRulesPath + ".bak", validBackup);

        // 2. Act
        var store = new ApprovalRuleStore(_globalRulesPath);

        // 3. Assert: Successfully recovered rule from backup
        var rules = store.GetRules(ApprovalRuleScope.Global);
        Assert.Single(rules);
        Assert.Equal("rule-recovered", rules[0].Id);
    }

    [Fact]
    public void CorruptedJson_WithNoBackup_ReturnsEmpty()
    {
        File.WriteAllText(_globalRulesPath, "BROKEN JSON");

        var store = new ApprovalRuleStore(_globalRulesPath);
        var rules = store.GetRules(ApprovalRuleScope.Global);
        Assert.Empty(rules);
    }

    [Fact]
    public void PruneList_EnforcesMaxRulesPerScope_LRU()
    {
        var store = new ApprovalRuleStore(_globalRulesPath);

        // Add 205 conversation rules
        for (int i = 1; i <= 205; i++)
        {
            var rule = ApprovalRule.CreateExact(
                ApprovalRuleScope.Conversation,
                $"cmd-{i}",
                conversationId: "conv-1");
            rule.LastUsedAtUtc = DateTimeOffset.UtcNow.AddMinutes(i); // higher index = more recently used
            store.AddRule(rule);
        }

        var rules = store.GetRules(ApprovalRuleScope.Conversation);
        Assert.Equal(ApprovalRuleStore.MaxRulesPerScope, rules.Count);

        // Most recently used should be preserved
        Assert.Contains(rules, r => r.CommandPattern == "cmd-205");
        Assert.Contains(rules, r => r.CommandPattern == "cmd-6");
        Assert.DoesNotContain(rules, r => r.CommandPattern == "cmd-1"); // Oldest 5 pruned
    }

    [Fact]
    public void RemoveRule_RemovesFromCorrectScope()
    {
        var store = new ApprovalRuleStore(_globalRulesPath);

        var rule = ApprovalRule.CreateExact(ApprovalRuleScope.Global, "npm test");
        store.AddRule(rule);

        Assert.Single(store.GetRules(ApprovalRuleScope.Global));

        bool removed = store.RemoveRule(rule.Id);
        Assert.True(removed);
        Assert.Empty(store.GetRules(ApprovalRuleScope.Global));
    }

    [Fact]
    public void ClearRules_ClearsSpecifiedScope()
    {
        var store = new ApprovalRuleStore(_globalRulesPath);

        store.AddRule(ApprovalRule.CreateExact(ApprovalRuleScope.Conversation, "c1", conversationId: "conv-1"));
        store.AddRule(ApprovalRule.CreateExact(ApprovalRuleScope.Global, "g1"));

        store.ClearRules(ApprovalRuleScope.Conversation);

        Assert.Empty(store.GetRules(ApprovalRuleScope.Conversation));
        Assert.Single(store.GetRules(ApprovalRuleScope.Global));
    }

    [Fact]
    public void RecordUsage_IncrementsCount_AndUpdatesTimestamp()
    {
        var store = new ApprovalRuleStore(_globalRulesPath);

        var rule = ApprovalRule.CreateExact(ApprovalRuleScope.Global, "git fetch");
        store.AddRule(rule);

        Assert.Equal(0, rule.UseCount);
        var initialLastUsed = rule.LastUsedAtUtc;

        store.RecordUsage(rule.Id);

        var updated = store.GetRules(ApprovalRuleScope.Global)[0];
        Assert.Equal(1, updated.UseCount);
        Assert.True(updated.LastUsedAtUtc >= initialLastUsed);
    }

    [Fact]
    public void FindMatchingRule_FindsInScopeOrder_ConversationProjectGlobal()
    {
        string projectDir = Path.Combine(_tempDir, "proj");
        Directory.CreateDirectory(projectDir);

        var store = new ApprovalRuleStore(_globalRulesPath);

        var convRule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Conversation,
            "cmd",
            conversationId: "conv-x");

        var projRule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Project,
            "cmd",
            workspaceKey: projectDir);

        var globalRule = ApprovalRule.CreateExact(
            ApprovalRuleScope.Global,
            "cmd");

        store.AddRule(globalRule);
        store.AddRule(projRule, projectDir);
        store.AddRule(convRule);

        var req = new ApprovalRequest(
            id: "r1",
            conversationId: "conv-x",
            stepIdx: 1,
            toolName: "run_command",
            commandLine: "cmd",
            workspacePaths: [projectDir]);

        // Conversation should win first
        var match = store.FindMatchingRule(req);
        Assert.NotNull(match);
        Assert.Equal(ApprovalRuleScope.Conversation, match.Scope);

        // Without matching conversation, project should win
        var reqOtherConv = new ApprovalRequest(
            id: "r2",
            conversationId: "conv-other",
            stepIdx: 1,
            toolName: "run_command",
            commandLine: "cmd",
            workspacePaths: [projectDir]);

        var matchProj = store.FindMatchingRule(reqOtherConv);
        Assert.NotNull(matchProj);
        Assert.Equal(ApprovalRuleScope.Project, matchProj.Scope);
    }
}
