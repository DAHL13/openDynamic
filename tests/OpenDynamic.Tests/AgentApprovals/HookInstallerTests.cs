using System.Text.Json;
using System.Text.Json.Nodes;
using OpenDynamic.Core.AgentApprovals;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class HookInstallerTests : IDisposable
{
    private readonly string _tempDir;

    public HookInstallerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "openDynamic-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore temp cleanup errors
        }
    }

    [Fact]
    public void FormatCommand_QuotesPathsWithSpaces()
    {
        string pathWithSpaces = @"C:\Program Files\openDynamic\hook\OpenDynamic.Hook.exe";
        string formatted = AntigravityHookInstaller.FormatCommand(pathWithSpaces);
        Assert.Equal($"\"{pathWithSpaces}\"", formatted);

        string pathWithoutSpaces = @"C:\openDynamic\hook\OpenDynamic.Hook.exe";
        string formattedNoSpaces = AntigravityHookInstaller.FormatCommand(pathWithoutSpaces);
        Assert.Equal(pathWithoutSpaces, formattedNoSpaces);
    }

    [Fact]
    public void Install_MergesNonDestructively_AndPreservesExistingHooks()
    {
        string hooksFile = Path.Combine(_tempDir, "hooks.json");
        string existingContent = """
        {
          "existing-linter": {
            "PostToolUse": [
              {
                "matcher": "run_command",
                "hooks": [{ "command": "./lint.sh" }]
              }
            ]
          }
        }
        """;
        File.WriteAllText(hooksFile, existingContent);

        string hookExe = @"C:\Tools\OpenDynamic.Hook.exe";
        var result = AntigravityHookInstaller.Install(hooksFile, hookExe);

        Assert.True(result.Success);
        Assert.NotNull(result.BackupPath);
        Assert.True(File.Exists(result.BackupPath));

        // Verify backup contains original content
        string backupContent = File.ReadAllText(result.BackupPath);
        Assert.Contains("existing-linter", backupContent);
        Assert.DoesNotContain("openDynamic-approvals", backupContent);

        // Verify new file contains BOTH existing-linter and openDynamic-approvals
        string mergedContent = File.ReadAllText(hooksFile);
        var root = JsonNode.Parse(mergedContent) as JsonObject;
        Assert.NotNull(root);
        Assert.True(root.ContainsKey("existing-linter"));
        Assert.True(root.ContainsKey("openDynamic-approvals"));

        // Verify hook structure
        var odHook = root["openDynamic-approvals"]?["PreToolUse"]?[0];
        Assert.NotNull(odHook);
        Assert.Equal(AntigravityHookInstaller.DefaultMatcher, odHook["matcher"]?.GetValue<string>());
        var innerHook = odHook["hooks"]?[0];
        Assert.NotNull(innerHook);
        Assert.Equal(hookExe, innerHook["command"]?.GetValue<string>());
        Assert.Equal(120, innerHook["timeout"]?.GetValue<int>());
    }

    [Fact]
    public void Install_IsIdempotent()
    {
        string hooksFile = Path.Combine(_tempDir, "hooks.json");
        string hookExe = @"C:\Tools\OpenDynamic.Hook.exe";

        var res1 = AntigravityHookInstaller.Install(hooksFile, hookExe);
        Assert.True(res1.Success);

        var res2 = AntigravityHookInstaller.Install(hooksFile, hookExe);
        Assert.True(res2.Success);

        string content = File.ReadAllText(hooksFile);
        using var doc = JsonDocument.Parse(content);
        // Ensure openDynamic-approvals is present exactly once
        int count = 0;
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Name == "openDynamic-approvals") count++;
        }
        Assert.Equal(1, count);
    }

    [Fact]
    public void Install_AbortsWithoutModifyingFile_IfExistingJsonIsInvalid()
    {
        string hooksFile = Path.Combine(_tempDir, "hooks.json");
        string corruptJson = "{ this is completely invalid json !!! ";
        File.WriteAllText(hooksFile, corruptJson);

        string hookExe = @"C:\Tools\OpenDynamic.Hook.exe";
        var result = AntigravityHookInstaller.Install(hooksFile, hookExe);

        Assert.False(result.Success);
        Assert.Contains("inválido", result.Message, StringComparison.OrdinalIgnoreCase);

        // File remains unchanged
        string fileAfter = File.ReadAllText(hooksFile);
        Assert.Equal(corruptJson, fileAfter);
    }

    [Fact]
    public void Uninstall_RemovesOnlyOpenDynamicKey()
    {
        string hooksFile = Path.Combine(_tempDir, "hooks.json");
        string initialContent = """
        {
          "my-hook": { "enabled": true },
          "openDynamic-approvals": { "enabled": true }
        }
        """;
        File.WriteAllText(hooksFile, initialContent);

        var result = AntigravityHookInstaller.Uninstall(hooksFile);
        Assert.True(result.Success);

        string remainingContent = File.ReadAllText(hooksFile);
        var root = JsonNode.Parse(remainingContent) as JsonObject;
        Assert.NotNull(root);
        Assert.True(root.ContainsKey("my-hook"));
        Assert.False(root.ContainsKey("openDynamic-approvals"));
    }
}
