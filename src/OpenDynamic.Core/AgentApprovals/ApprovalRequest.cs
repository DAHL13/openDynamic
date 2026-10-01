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
        string clean = json.Trim().Trim('\uFEFF');

        using var doc = JsonDocument.Parse(clean);
        var root = doc.RootElement;

        // If payload is a raw Antigravity toolCall payload, delegate to FromHookPayload
        if (root.TryGetProperty("toolCall", out _) || root.TryGetProperty("ToolCall", out _))
        {
            string? id = null;
            if (root.TryGetProperty("id", out var idProp) || root.TryGetProperty("Id", out idProp) ||
                root.TryGetProperty("request_id", out idProp))
            {
                id = idProp.GetString();
            }
            return FromHookPayload(clean, id);
        }

        // Support camelCase, PascalCase, and snake_case properties
        string generatedId = Guid.NewGuid().ToString("N");
        if (root.TryGetProperty("id", out var idP) || root.TryGetProperty("Id", out idP) ||
            root.TryGetProperty("request_id", out idP) || root.TryGetProperty("requestId", out idP))
        {
            var idVal = idP.GetString();
            if (!string.IsNullOrWhiteSpace(idVal)) generatedId = idVal;
        }

        string? convId = null;
        if (root.TryGetProperty("conversationId", out var cP) || root.TryGetProperty("ConversationId", out cP) ||
            root.TryGetProperty("conversation_id", out cP))
        {
            convId = cP.GetString();
        }

        int? step = null;
        if (root.TryGetProperty("stepIdx", out var sP) || root.TryGetProperty("StepIdx", out sP) ||
            root.TryGetProperty("step_idx", out sP))
        {
            if (sP.TryGetInt32(out int sVal)) step = sVal;
        }

        string tool = "unknown_tool";
        if (root.TryGetProperty("toolName", out var tP) || root.TryGetProperty("ToolName", out tP) ||
            root.TryGetProperty("tool_name", out tP) || root.TryGetProperty("tool", out tP))
        {
            tool = tP.GetString() ?? "unknown_tool";
        }

        string? cmd = null;
        if (root.TryGetProperty("commandLine", out var cmdP) || root.TryGetProperty("CommandLine", out cmdP) ||
            root.TryGetProperty("command_line", out cmdP) || root.TryGetProperty("command", out cmdP))
        {
            cmd = cmdP.GetString();
        }

        string? cwd = null;
        if (root.TryGetProperty("cwd", out var cwdP) || root.TryGetProperty("Cwd", out cwdP) ||
            root.TryGetProperty("working_directory", out cwdP))
        {
            cwd = cwdP.GetString();
        }

        string? targetFile = null;
        if (root.TryGetProperty("targetFile", out var tfP) || root.TryGetProperty("TargetFile", out tfP) ||
            root.TryGetProperty("target_file", out tfP) || root.TryGetProperty("file", out tfP))
        {
            targetFile = tfP.GetString();
        }

        string? instruction = null;
        if (root.TryGetProperty("instruction", out var insP) || root.TryGetProperty("Instruction", out insP))
        {
            instruction = insP.GetString();
        }

        string? codeContent = null;
        if (root.TryGetProperty("codeContent", out var codeP) || root.TryGetProperty("CodeContent", out codeP) ||
            root.TryGetProperty("code_content", out codeP))
        {
            codeContent = codeP.GetString();
        }

        string? rawArgsJson = null;
        if (root.TryGetProperty("rawArgsJson", out var rawP) || root.TryGetProperty("raw_args_json", out rawP) ||
            root.TryGetProperty("args", out rawP))
        {
            rawArgsJson = rawP.ValueKind == JsonValueKind.String ? rawP.GetString() : rawP.GetRawText();
        }

        var workspaces = new List<string>();
        if (root.TryGetProperty("workspacePaths", out var wsP) || root.TryGetProperty("WorkspacePaths", out wsP) ||
            root.TryGetProperty("workspace_paths", out wsP))
        {
            if (wsP.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in wsP.EnumerateArray())
                {
                    var p = item.GetString();
                    if (!string.IsNullOrWhiteSpace(p)) workspaces.Add(p);
                }
            }
        }

        return new ApprovalRequest(
            id: generatedId,
            conversationId: convId,
            stepIdx: step,
            toolName: tool,
            commandLine: cmd,
            cwd: cwd,
            targetFile: targetFile,
            instruction: instruction,
            codeContent: codeContent,
            rawArgsJson: rawArgsJson,
            workspacePaths: workspaces);
    }
}
