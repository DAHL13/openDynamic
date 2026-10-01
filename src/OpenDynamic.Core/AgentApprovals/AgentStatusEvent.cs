using System.Text.Json;
using System.Text.Json.Nodes;

namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Domain model representing an Antigravity agent lifecycle event (e.g. Stop hook) (Task 8).
/// </summary>
public sealed class AgentStatusEvent
{
    public string ConversationId { get; init; } = string.Empty;
    public IReadOnlyList<string> WorkspacePaths { get; init; } = Array.Empty<string>();
    public string WorkspaceFolder { get; init; } = "Workspace";
    public bool FullyIdle { get; init; }
    public string TerminationReason { get; init; } = string.Empty;
    public int ExecutionNum { get; init; }
    public string? Error { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    /// <summary>
    /// Parses an AgentStatusEvent from the Antigravity Stop hook JSON payload.
    /// </summary>
    public static AgentStatusEvent FromHookPayload(string jsonPayload)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonPayload);

        var node = JsonNode.Parse(jsonPayload) ?? throw new JsonException("El payload de evento es nulo.");

        string convId = node["conversationId"]?.GetValue<string>() ?? string.Empty;
        bool fullyIdle = node["fullyIdle"]?.GetValue<bool>() ?? true;
        string termReason = node["terminationReason"]?.GetValue<string>() ?? string.Empty;
        int execNum = node["executionNum"]?.GetValue<int>() ?? 0;
        string? error = node["error"]?.GetValue<string>();

        var workspaceList = new List<string>();
        if (node["workspacePaths"] is JsonArray arr)
        {
            foreach (var item in arr)
            {
                if (item != null) workspaceList.Add(item.ToString());
            }
        }

        string folderName = "Workspace";
        if (workspaceList.Count > 0 && !string.IsNullOrWhiteSpace(workspaceList[0]))
        {
            folderName = ApprovalPresentation.GetFolderName(workspaceList[0]);
        }

        return new AgentStatusEvent
        {
            ConversationId = convId,
            WorkspacePaths = workspaceList.AsReadOnly(),
            WorkspaceFolder = folderName,
            FullyIdle = fullyIdle,
            TerminationReason = termReason,
            ExecutionNum = execNum,
            Error = error
        };
    }

    public string ToJson()
    {
        return JsonSerializer.Serialize(this, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });
    }

    public static AgentStatusEvent FromJson(string json)
    {
        return JsonSerializer.Deserialize<AgentStatusEvent>(json, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        }) ?? throw new JsonException("Deserialized AgentStatusEvent was null.");
    }
}
