namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Represents an authorization rule allowing automated approval of specified commands
/// without showing interactive prompt dialogs (Golden Rules 12 &amp; 13).
/// </summary>
public sealed class ApprovalRule
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public ApprovalRuleScope Scope { get; init; } = ApprovalRuleScope.Project;
    public string ToolName { get; init; } = "run_command";
    public string CommandPattern { get; init; } = string.Empty;
    public bool IsPrefixMatch { get; init; }
    public string? ConversationId { get; init; }
    public string? WorkspaceKey { get; set; }
    public string? WorkspaceFolder { get; set; }
    public DateTimeOffset CreatedAtUtc { get; init; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastUsedAtUtc { get; set; } = DateTimeOffset.UtcNow;
    public int UseCount { get; set; }

    /// <summary>
    /// Gets a short summary of the rule command pattern suitable for UI display (max 60 chars).
    /// </summary>
    public string DisplaySummary
    {
        get
        {
            if (string.IsNullOrWhiteSpace(CommandPattern)) return "(vacío)";
            string prefixNotice = IsPrefixMatch ? " [prefijo]" : string.Empty;
            string pattern = CommandPattern.Length <= 50
                ? CommandPattern
                : CommandPattern[..47] + "...";
            return pattern + prefixNotice;
        }
    }

    public static ApprovalRule CreateExact(
        ApprovalRuleScope scope,
        string commandPattern,
        string? conversationId = null,
        string? workspaceKey = null,
        string? workspaceFolder = null)
    {
        return new ApprovalRule
        {
            Scope = scope,
            ToolName = "run_command",
            CommandPattern = commandPattern,
            IsPrefixMatch = false,
            ConversationId = conversationId,
            WorkspaceKey = string.IsNullOrWhiteSpace(workspaceKey) ? null : ApprovalRuleMatcher.NormalizePath(workspaceKey),
            WorkspaceFolder = workspaceFolder
        };
    }

    public static ApprovalRule CreatePrefix(
        ApprovalRuleScope scope,
        string prefixPattern,
        string? workspaceKey = null,
        string? workspaceFolder = null)
    {
        return new ApprovalRule
        {
            Scope = scope,
            ToolName = "run_command",
            CommandPattern = prefixPattern,
            IsPrefixMatch = true,
            WorkspaceKey = string.IsNullOrWhiteSpace(workspaceKey) ? null : ApprovalRuleMatcher.NormalizePath(workspaceKey),
            WorkspaceFolder = workspaceFolder
        };
    }
}
