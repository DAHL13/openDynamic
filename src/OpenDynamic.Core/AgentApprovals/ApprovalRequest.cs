using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Domain model representing an agent approval request intercepted by Antigravity hooks.
/// </summary>
public sealed record ApprovalRequest
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public string ConversationId { get; init; } = string.Empty;
    public int? StepIdx { get; init; }
    public string ToolName { get; init; } = string.Empty;
    public string? CommandLine { get; init; }
    public string? Cwd { get; init; }
    public string? TargetFile { get; init; }
    public string? Instruction { get; init; }
    public string? CodeContent { get; init; }
    public string? RawArgsJson { get; init; }
    public IReadOnlyList<string> WorkspacePaths { get; init; } = Array.Empty<string>();
    public string? TranscriptPath { get; init; }
    public string? ArtifactDirectoryPath { get; init; }
    public string? ModelName { get; init; }
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;

    public ApprovalRequest()
    {
    }

    public ApprovalRequest(
        string? id,
        string? conversationId,
        int? stepIdx,
        string? toolName,
        string? commandLine = null,
        string? cwd = null,
        string? targetFile = null,
        string? instruction = null,
        string? codeContent = null,
        string? rawArgsJson = null,
        IReadOnlyList<string>? workspacePaths = null,
        string? transcriptPath = null,
        string? artifactDirectoryPath = null,
        string? modelName = null,
        DateTimeOffset? timestampUtc = null)
    {
        Id = string.IsNullOrWhiteSpace(id) ? Guid.NewGuid().ToString("N") : id;
        ConversationId = conversationId?.Trim() ?? string.Empty;
        StepIdx = stepIdx;
        ToolName = toolName?.Trim() ?? "unknown_tool";
        CommandLine = commandLine;
        Cwd = cwd;
        TargetFile = targetFile;
        Instruction = instruction;
        CodeContent = codeContent;
        RawArgsJson = rawArgsJson;
        WorkspacePaths = workspacePaths ?? Array.Empty<string>();
        TranscriptPath = transcriptPath;
        ArtifactDirectoryPath = artifactDirectoryPath;
        ModelName = modelName;
        TimestampUtc = timestampUtc ?? DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Parses an Antigravity PreToolUse hook payload (from stdin) into an <see cref="ApprovalRequest"/>.
    /// </summary>
    /// <param name="json">Hook payload JSON received on stdin.</param>
    /// <param name="generatedId">Optional predefined request ID.</param>
    /// <returns>Populated <see cref="ApprovalRequest"/>.</returns>
    /// <exception cref="JsonException">Thrown if json is invalid or missing essential structure.</exception>
    public static ApprovalRequest FromHookPayload(string json, string? generatedId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        string? conversationId = null;
        if (root.TryGetProperty("conversationId", out var convProp) || root.TryGetProperty("ConversationId", out convProp))
        {
            conversationId = convProp.GetString();
        }

        int? stepIdx = null;
        if (root.TryGetProperty("stepIdx", out var stepProp) || root.TryGetProperty("StepIdx", out stepProp))
        {
            if (stepProp.TryGetInt32(out var val))
            {
                stepIdx = val;
            }
        }

        string? modelName = null;
        if (root.TryGetProperty("modelName", out var modelProp) || root.TryGetProperty("ModelName", out modelProp))
        {
            modelName = modelProp.GetString();
        }

        string? transcriptPath = null;
        if (root.TryGetProperty("transcriptPath", out var transProp) || root.TryGetProperty("TranscriptPath", out transProp))
        {
            transcriptPath = transProp.GetString();
        }

        string? artifactDirectoryPath = null;
        if (root.TryGetProperty("artifactDirectoryPath", out var artProp) || root.TryGetProperty("ArtifactDirectoryPath", out artProp))
        {
            artifactDirectoryPath = artProp.GetString();
        }

        var workspacePaths = new List<string>();
        if (root.TryGetProperty("workspacePaths", out var wsProp) || root.TryGetProperty("WorkspacePaths", out wsProp))
        {
            if (wsProp.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in wsProp.EnumerateArray())
                {
                    var p = item.GetString();
                    if (!string.IsNullOrWhiteSpace(p))
                    {
                        workspacePaths.Add(p);
                    }
                }
            }
        }

        string toolName = "unknown_tool";
        string? commandLine = null;
        string? cwd = null;
        string? targetFile = null;
        string? instruction = null;
        string? codeContent = null;
        string? rawArgsJson = null;

        if (root.TryGetProperty("toolCall", out var toolCallProp) || root.TryGetProperty("ToolCall", out toolCallProp))
        {
            if (toolCallProp.TryGetProperty("name", out var nameProp) || toolCallProp.TryGetProperty("Name", out nameProp))
            {
                toolName = nameProp.GetString() ?? "unknown_tool";
            }

            if (toolCallProp.TryGetProperty("args", out var argsProp) || toolCallProp.TryGetProperty("Args", out argsProp))
            {
                rawArgsJson = argsProp.GetRawText();

                if (argsProp.ValueKind == JsonValueKind.Object)
                {
                    foreach (var prop in argsProp.EnumerateObject())
                    {
                        var propName = prop.Name;
                        if (propName.Equals("CommandLine", StringComparison.OrdinalIgnoreCase))
                        {
                            commandLine = prop.Value.GetString();
                        }
                        else if (propName.Equals("Cwd", StringComparison.OrdinalIgnoreCase))
                        {
                            cwd = prop.Value.GetString();
                        }
                        else if (propName.Equals("TargetFile", StringComparison.OrdinalIgnoreCase))
                        {
                            targetFile = prop.Value.GetString();
                        }
                        else if (propName.Equals("Instruction", StringComparison.OrdinalIgnoreCase))
                        {
                            instruction = prop.Value.GetString();
                        }
                        else if (propName.Equals("CodeContent", StringComparison.OrdinalIgnoreCase))
                        {
                            codeContent = prop.Value.GetString();
                        }
                    }
                }
            }
        }

        return new ApprovalRequest(
            id: generatedId,
            conversationId: conversationId,
            stepIdx: stepIdx,
            toolName: toolName,
            commandLine: commandLine,
            cwd: cwd,
            targetFile: targetFile,
            instruction: instruction,
            codeContent: codeContent,
            rawArgsJson: rawArgsJson,
            workspacePaths: workspacePaths,
            transcriptPath: transcriptPath,
            artifactDirectoryPath: artifactDirectoryPath,
            modelName: modelName);
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public static ApprovalRequest FromJson(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);
        var parsed = JsonSerializer.Deserialize<ApprovalRequest>(json, JsonOptions);
        return parsed ?? throw new JsonException("Deserialized ApprovalRequest was null.");
    }
}
