namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Defines the persistence and evaluation scope of an approval rule.
/// </summary>
public enum ApprovalRuleScope
{
    /// <summary>
    /// Ephemeral rule stored strictly in memory for the active conversation ID.
    /// Destroyed when the application or conversation ends.
    /// </summary>
    Conversation,

    /// <summary>
    /// Persistent rule stored for the specific project workspace.
    /// Saved in .antigravity/approval-rules.json in the workspace root.
    /// </summary>
    Project,

    /// <summary>
    /// Persistent rule stored globally across all projects on the current machine.
    /// Saved in %USERPROFILE%/.antigravity/approval-rules.json.
    /// </summary>
    Global
}
