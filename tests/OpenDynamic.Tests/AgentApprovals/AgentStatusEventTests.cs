using OpenDynamic.Core.AgentApprovals;
using Xunit;

namespace OpenDynamic.Tests.AgentApprovals;

public class AgentStatusEventTests
{
    [Fact]
    public void FromHookPayload_ParsesFullyIdleTrue()
    {
        string payload = """
        {
          "conversationId": "conv-xyz-123",
          "executionNum": 4,
          "terminationReason": "completed",
          "fullyIdle": true,
          "workspacePaths": ["C:\\Users\\Pcrz\\Projects\\openDynamic"]
        }
        """;

        var evt = AgentStatusEvent.FromHookPayload(payload);

        Assert.Equal("conv-xyz-123", evt.ConversationId);
        Assert.Equal(4, evt.ExecutionNum);
        Assert.Equal("completed", evt.TerminationReason);
        Assert.True(evt.FullyIdle);
        Assert.Equal("openDynamic", evt.WorkspaceFolder);
        Assert.Null(evt.Error);
    }

    [Fact]
    public void FromHookPayload_ParsesFullyIdleFalse()
    {
        string payload = """
        {
          "conversationId": "conv-active",
          "fullyIdle": false,
          "terminationReason": "subtask_complete"
        }
        """;

        var evt = AgentStatusEvent.FromHookPayload(payload);

        Assert.Equal("conv-active", evt.ConversationId);
        Assert.False(evt.FullyIdle);
        Assert.Equal("subtask_complete", evt.TerminationReason);
        Assert.Equal("Workspace", evt.WorkspaceFolder);
    }

    [Fact]
    public void FromHookPayload_ResolvesFolderNameFromWorkspacePaths()
    {
        string payload = """
        {
          "conversationId": "conv-proj",
          "workspacePaths": ["D:/Workspaces/CoolService/"]
        }
        """;

        var evt = AgentStatusEvent.FromHookPayload(payload);

        Assert.Equal("CoolService", evt.WorkspaceFolder);
    }

    [Fact]
    public void RoundTrip_JsonSerialization()
    {
        var evt = new AgentStatusEvent
        {
            ConversationId = "conv-999",
            WorkspacePaths = [@"C:\Code\MyProject"],
            WorkspaceFolder = "MyProject",
            FullyIdle = true,
            TerminationReason = "user_aborted",
            ExecutionNum = 12,
            Error = "Some non-fatal warning"
        };

        string json = evt.ToJson();
        var deserialized = AgentStatusEvent.FromJson(json);

        Assert.Equal(evt.ConversationId, deserialized.ConversationId);
        Assert.Equal(evt.WorkspaceFolder, deserialized.WorkspaceFolder);
        Assert.Equal(evt.FullyIdle, deserialized.FullyIdle);
        Assert.Equal(evt.TerminationReason, deserialized.TerminationReason);
        Assert.Equal(evt.ExecutionNum, deserialized.ExecutionNum);
        Assert.Equal(evt.Error, deserialized.Error);
    }

    [Fact]
    public void PipeMessage_CreateStatus_RoundTrip()
    {
        var evt = new AgentStatusEvent
        {
            ConversationId = "conv-pipe",
            FullyIdle = true,
            WorkspaceFolder = "Repo1"
        };

        var pipeMsg = PipeMessage.CreateStatus(evt);
        Assert.Equal("status", pipeMsg.Type);

        string serialized = pipeMsg.Serialize();
        var deserializedMsg = PipeMessage.Deserialize(serialized);

        Assert.Equal("status", deserializedMsg.Type);
        Assert.NotNull(deserializedMsg.Payload);

        var parsedEvt = AgentStatusEvent.FromJson(deserializedMsg.Payload);
        Assert.Equal("conv-pipe", parsedEvt.ConversationId);
        Assert.True(parsedEvt.FullyIdle);
        Assert.Equal("Repo1", parsedEvt.WorkspaceFolder);
    }
}
