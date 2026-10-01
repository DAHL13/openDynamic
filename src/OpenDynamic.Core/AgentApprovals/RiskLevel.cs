namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Indicates the safety and destructiveness risk level of an agent tool execution.
/// </summary>
public enum RiskLevel
{
    /// <summary>
    /// Harmless or read-only commands (e.g. git status, ls, dir, echo).
    /// </summary>
    Low = 0,

    /// <summary>
    /// File writes or chained/piped commands (e.g. write_to_file, |, &amp;&amp;, ;, redirections).
    /// </summary>
    Medium = 1,

    /// <summary>
    /// Destructive actions, recursive deletions, forced pushes, system alterations or script execution.
    /// Requires expanded review before approval and disables one-key hotkey approval.
    /// </summary>
    High = 2
}
