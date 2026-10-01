namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Pure matcher evaluating whether an incoming approval request satisfies an authorization rule (Task 1 &amp; 6b).
/// Enforces exact command match, strict scope boundaries, and zero auto-approval for High-risk commands.
/// </summary>
public static class ApprovalRuleMatcher
{
    /// <summary>
    /// Evaluates if an incoming approval request matches the given approval rule.
    /// </summary>
    public static bool Matches(ApprovalRule rule, ApprovalRequest request, RiskLevel risk = RiskLevel.Low)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(request);

        // Golden Rule 12: High-risk commands can NEVER be auto-approved or matched by rules.
        if (risk == RiskLevel.High)
        {
            return false;
        }

        // Only run_command is supported for rules in v1
        if (!string.Equals(rule.ToolName, "run_command", StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(request.ToolName, "run_command", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Scope validation
        if (!MatchesScope(rule, request))
        {
            return false;
        }

        // Command pattern matching
        if (rule.IsPrefixMatch)
        {
            // Safe prefix rules are only valid for Project scope
            if (rule.Scope != ApprovalRuleScope.Project)
            {
                return false;
            }

            return SafePrefixMatcher.MatchesRule(request.CommandLine, rule.CommandPattern, risk);
        }

        // Exact match
        return MatchesExactCommand(rule.CommandPattern, request.CommandLine);
    }

    /// <summary>
    /// Normalizes command string by trimming whitespace and normalizing line breaks.
    /// Internal spaces and character casing are strictly preserved.
    /// </summary>
    public static string NormalizeCommand(string? command)
    {
        if (string.IsNullOrEmpty(command)) return string.Empty;
        return command.Trim().Replace("\r\n", "\n");
    }

    private static bool MatchesExactCommand(string? pattern, string? incoming)
    {
        string normPattern = NormalizeCommand(pattern);
        string normIncoming = NormalizeCommand(incoming);

        if (string.IsNullOrEmpty(normPattern) || string.IsNullOrEmpty(normIncoming))
        {
            return false;
        }

        // Exact character-by-character match
        return string.Equals(normPattern, normIncoming, StringComparison.Ordinal);
    }

    private static bool MatchesScope(ApprovalRule rule, ApprovalRequest request)
    {
        return rule.Scope switch
        {
            ApprovalRuleScope.Global => true,

            ApprovalRuleScope.Conversation =>
                !string.IsNullOrWhiteSpace(rule.ConversationId) &&
                string.Equals(rule.ConversationId, request.ConversationId, StringComparison.OrdinalIgnoreCase),

            ApprovalRuleScope.Project => MatchesProjectScope(rule, request),

            _ => false
        };
    }

    private static bool MatchesProjectScope(ApprovalRule rule, ApprovalRequest request)
    {
        // 1. If rule has a workspace key (normalized path)
        if (!string.IsNullOrWhiteSpace(rule.WorkspaceKey))
        {
            string normalizedRuleKey = NormalizePath(rule.WorkspaceKey);

            foreach (var wp in request.WorkspacePaths)
            {
                if (string.Equals(NormalizePath(wp), normalizedRuleKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            if (!string.IsNullOrWhiteSpace(request.Cwd))
            {
                string normCwd = NormalizePath(request.Cwd);
                if (normCwd.StartsWith(normalizedRuleKey, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }

        // 2. Fallback to workspace folder name if key is not available
        if (!string.IsNullOrWhiteSpace(rule.WorkspaceFolder) && !string.IsNullOrWhiteSpace(request.WorkspaceFolder))
        {
            return string.Equals(rule.WorkspaceFolder, request.WorkspaceFolder, StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    public static string NormalizePath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return string.Empty;
        return path.Trim().Replace('\\', '/').TrimEnd('/');
    }
}
