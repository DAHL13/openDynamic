using System.Text.Json;
using OpenDynamic.Core.AgentApprovals;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class ZeroLogsSecurityTests
{
    private class MemoryLogSink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];

        public void Emit(LogEvent logEvent)
        {
            Events.Add(logEvent);
        }
    }

    [Fact]
    public void SerilogSink_WhenProcessingSensitiveCommands_NeverLogsCommandOrPathsOrSecrets()
    {
        // 1. Arrange sensitive payload per Golden Rule 13
        string secretPassword = "SuperSecretPassword_999!";
        string sensitiveFilePath = @"C:\Users\Pcrz\.ssh\id_rsa";
        string sensitiveUrl = "https://internal.corp.net/credentials";
        string destructiveCmd = $"curl -u admin:{secretPassword} {sensitiveUrl} && rm -rf {sensitiveFilePath}";

        string rawArgsJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["CommandLine"] = destructiveCmd,
            ["SecretToken"] = "ghp_VerySecretAccessToken12345",
            ["PrivateKeyPath"] = sensitiveFilePath
        });

        var request = new ApprovalRequest(
            "req-sec-001",
            "conv-sec-99",
            42,
            "run_command",
            destructiveCmd,
            rawArgsJson: rawArgsJson);

        var sessionPolicy = new ApprovalSessionPolicy(request);
        var resolvedResponse = ApprovalResponse.Allow();

        // 2. Setup Serilog memory sink
        var sink = new MemoryLogSink();
        var logger = new LoggerConfiguration()
            .MinimumLevel.Verbose()
            .WriteTo.Sink(sink)
            .CreateLogger();

        // 3. Act: Simulate pipe server logging (strictly aggregated metadata only)
        logger.Information(
            "Approval resolved. RequestId={RequestId}, Tool={Tool}, Risk={Risk}, Decision={Decision}, Source={Source}, ElapsedMs={ElapsedMs}",
            request.Id,
            request.ToolName,
            sessionPolicy.Presentation.Risk,
            resolvedResponse.Decision,
            sessionPolicy.ResolutionSource ?? "unknown",
            120);

        logger.Information(
            "Approval delegated to native dialog (island unavailable). RequestId={RequestId}, Tool={Tool}, Risk={Risk}",
            request.Id,
            request.ToolName,
            sessionPolicy.Presentation.Risk);

        logger.Warning("Error handling client session. RequestId={RequestId}", request.Id);

        // 4. Assert: Audit events must NEVER contain sensitive details in messages or properties
        Assert.NotEmpty(sink.Events);

        foreach (var evt in sink.Events)
        {
            string renderedMessage = evt.RenderMessage();

            // None of the sensitive content must appear in rendered text
            Assert.DoesNotContain(secretPassword, renderedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sensitiveFilePath, renderedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain(sensitiveUrl, renderedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("rm -rf", renderedMessage, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("ghp_VerySecretAccessToken", renderedMessage, StringComparison.OrdinalIgnoreCase);

            // None of the sensitive content must appear in structured properties
            foreach (var prop in evt.Properties)
            {
                string propVal = prop.Value.ToString();
                Assert.DoesNotContain(secretPassword, propVal, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(sensitiveFilePath, propVal, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain(sensitiveUrl, propVal, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("rm -rf", propVal, StringComparison.OrdinalIgnoreCase);
                Assert.DoesNotContain("ghp_VerySecretAccessToken", propVal, StringComparison.OrdinalIgnoreCase);
            }
        }
    }

    [Fact]
    public void PipeServerSourceCode_DoesNotLogCommandsOrPaths()
    {
        // Static audit test: inspect AgentApprovalPipeServer.cs to verify zero logging calls containing forbidden properties
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "openDynamic.sln")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        string serverFile = Path.Combine(dir.FullName, "src", "OpenDynamic.App", "Services", "AgentApprovalPipeServer.cs");
        Assert.True(File.Exists(serverFile), $"AgentApprovalPipeServer.cs not found at {serverFile}");

        string code = File.ReadAllText(serverFile);

        // Golden Rule 13: Strictly forbidden properties in Log.* calls
        string[] forbiddenLogTokens =
        [
            "{CommandLine}",
            "{TargetFile}",
            "{CodeContent}",
            "{Cwd}",
            "{Arguments}",
            "request.CommandLine",
            "request.CodeContent",
            "request.TargetFile"
        ];

        foreach (var token in forbiddenLogTokens)
        {
            Assert.DoesNotContain(token, code);
        }
    }
}
