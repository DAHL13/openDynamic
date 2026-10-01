using OpenDynamic.Core.AgentApprovals;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class ProtocolTests
{
    [Fact]
    public void ApprovalResponse_DefaultsToAsk_WhenUnknownDecisionProvided()
    {
        var response = new ApprovalResponse("some_random_thing", "some reason");
        Assert.Equal("ask", response.Decision);
        Assert.Equal("some reason", response.Reason);
    }

    [Fact]
    public void ApprovalResponse_FactoriesWorkCorrectly()
    {
        var allow = ApprovalResponse.Allow();
        Assert.Equal("allow", allow.Decision);
        Assert.Null(allow.Reason);

        var deny = ApprovalResponse.Deny("Test reason");
        Assert.Equal("deny", deny.Decision);
        Assert.Equal("Test reason", deny.Reason);

        var ask = ApprovalResponse.AskNative("Ask reason");
        Assert.Equal("ask", ask.Decision);
        Assert.Equal("Ask reason", ask.Reason);
    }

    [Fact]
    public void ApprovalRequest_ParsesAntigravityPreToolUsePayload()
    {
        string sampleJson = """
        {
          "toolCall": {
            "name": "run_command",
            "args": {
              "CommandLine": "git status",
              "Cwd": "C:\\Workspace",
              "WaitMsBeforeAsync": 500
            }
          },
          "stepIdx": 12,
          "conversationId": "conv-12345",
          "workspacePaths": ["C:\\Workspace"],
          "transcriptPath": "C:\\Workspace\\transcript.jsonl",
          "artifactDirectoryPath": "C:\\Workspace\\artifacts",
          "modelName": "auto"
        }
        """;

        var request = ApprovalRequest.FromHookPayload(sampleJson, "test-req-1");

        Assert.Equal("test-req-1", request.Id);
        Assert.Equal("conv-12345", request.ConversationId);
        Assert.Equal(12, request.StepIdx);
        Assert.Equal("run_command", request.ToolName);
        Assert.Equal("git status", request.CommandLine);
        Assert.Equal("C:\\Workspace", request.Cwd);
        Assert.Single(request.WorkspacePaths);
        Assert.Equal("C:\\Workspace", request.WorkspacePaths[0]);
    }

    [Fact]
    public void PipeMessage_SerializesAndDeserializesCorrectly()
    {
        var request = new ApprovalRequest(
            id: "id-1",
            conversationId: "conv-1",
            stepIdx: 5,
            toolName: "run_command",
            commandLine: "dir");

        var msg = PipeMessage.CreateRequest(request);
        var json = msg.Serialize();

        var deserialized = PipeMessage.Deserialize(json);
        Assert.Equal(1, deserialized.Version);
        Assert.Equal("request", deserialized.Type);
        Assert.NotNull(deserialized.Payload);

        var parsedRequest = ApprovalRequest.FromJson(deserialized.Payload);
        Assert.Equal("id-1", parsedRequest.Id);
        Assert.Equal("run_command", parsedRequest.ToolName);
        Assert.Equal("dir", parsedRequest.CommandLine);
    }

    [Fact]
    public void PipeMessage_RejectsInvalidVersion()
    {
        string invalidVersionJson = """{"v": 2, "type": "ping", "payload": null}""";
        Assert.Throws<InvalidOperationException>(() => PipeMessage.Deserialize(invalidVersionJson));
    }

    [Fact]
    public void PipeMessage_EnforcesSizeLimit()
    {
        string giantPayload = new('A', 270 * 1024); // 270 KB > 256 KB
        var request = new ApprovalRequest(
            id: "id-big",
            conversationId: "conv-1",
            stepIdx: 1,
            toolName: "run_command",
            commandLine: giantPayload);

        Assert.Throws<InvalidOperationException>(() => PipeMessage.CreateRequest(request));
    }

    [Theory]
    [InlineData("""{"v": 1, "type": "request", "payload": "{\"toolName\":\"run_command\"}"}""")]
    [InlineData("""{"version": 1, "type": "request", "payload": "{\"toolName\":\"run_command\"}"}""")]
    [InlineData("""{"version": "1", "type": "request", "payload": "{\"toolName\":\"run_command\"}"}""")]
    [InlineData("""{"protocol_version": 1, "type": "request", "payload": "{\"toolName\":\"run_command\"}"}""")]
    [InlineData("""\uFEFF{"v": 1, "type": "request", "payload": "{\"toolName\":\"run_command\"}"}""")]
    public void PipeMessage_AcceptsFlexibleVersionFormatsAndBOM(string json)
    {
        string actualJson = json.Replace(@"\uFEFF", "\uFEFF");
        var msg = PipeMessage.Deserialize(actualJson);

        Assert.Equal(1, msg.Version);
        Assert.Equal("request", msg.Type);
        Assert.NotNull(msg.Payload);
    }

    [Fact]
    public void ApprovalRequest_FromJson_ParsesSnakeCaseAndCamelCase()
    {
        string snakeJson = """
        {
          "request_id": "test-req-1",
          "conversation_id": "conv-99",
          "step_idx": 3,
          "tool_name": "run_command",
          "command_line": "git status",
          "working_directory": "C:\\repo"
        }
        """;

        var request = ApprovalRequest.FromJson(snakeJson);
        Assert.Equal("test-req-1", request.Id);
        Assert.Equal("conv-99", request.ConversationId);
        Assert.Equal(3, request.StepIdx);
        Assert.Equal("run_command", request.ToolName);
        Assert.Equal("git status", request.CommandLine);
        Assert.Equal("C:\\repo", request.Cwd);
    }

    [Fact]
    public void SettingsViewModel_TestRequestPayload_IsValidUnderProtocolV1()
    {
        // Replicate exactly the payload generated by SettingsViewModel.SendTestRequestAsync
        var testReq = new ApprovalRequest(
            id: "test-abc12345",
            conversationId: "test-conversation",
            stepIdx: 1,
            toolName: "run_command",
            commandLine: "git status && git log -n 2 --oneline",
            cwd: @"C:\Work",
            workspacePaths: [@"C:\Work"]);

        var msg = PipeMessage.CreateRequest(testReq);
        string serialized = msg.Serialize();

        // 1. Verify envelope deserialization
        var deserializedMsg = PipeMessage.Deserialize(serialized);
        Assert.Equal(1, deserializedMsg.Version);
        Assert.Equal("request", deserializedMsg.Type);
        Assert.NotNull(deserializedMsg.Payload);

        // 2. Verify inner request deserialization by the server
        var parsedRequest = ApprovalRequest.FromJson(deserializedMsg.Payload);
        Assert.Equal("test-abc12345", parsedRequest.Id);
        Assert.Equal("run_command", parsedRequest.ToolName);
        Assert.Equal("git status && git log -n 2 --oneline", parsedRequest.CommandLine);

        // 3. Verify risk classification matches Medium for chained git commands
        var risk = CommandRiskClassifier.Classify(parsedRequest.ToolName, parsedRequest.CommandLine);
        Assert.Equal(RiskLevel.Medium, risk);
    }
}
