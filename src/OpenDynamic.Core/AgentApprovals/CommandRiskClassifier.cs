using System.Text.RegularExpressions;

namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Classifies tool calls and terminal commands into Low, Medium, or High risk levels.
/// High: Destructive file deletion, disk formatting, forced git operations, script execution, registry modifications, shutdown, elevation.
/// Medium: File modification tools, chained commands (&amp;&amp;, ;, ||), pipes (|), redirections (&gt;), subshells ($(), `).
/// Low: Harmless inspection and build commands (git status, dotnet build, npm test, etc.).
/// Guaranteed: Zero false "Low" for High-risk patterns (Golden Rule 12 &amp; 13).
/// </summary>
public static partial class CommandRiskClassifier
{
    private static readonly string[] FileWriteTools =
    [
        "write_to_file",
        "replace_file_content",
        "multi_replace_file_content"
    ];

    // High risk compiled regex patterns
    [GeneratedRegex(@"\brm\s+.*-(?:[a-z]*r[a-z]*f|[a-z]*f[a-z]*r)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RmRfPattern();

    [GeneratedRegex(@"\brm\s+.*-r\b.*-f\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RmSeparateRfPattern();

    [GeneratedRegex(@"\b(Remove-Item|ri)\b.*-(?:Recurse|r)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RemoveItemRecursePattern();

    [GeneratedRegex(@"\b(del|erase|rd|rmdir)\b.*\/s\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DelOrRdSlashSPattern();

    [GeneratedRegex(@"\b(format|diskpart|mkfs(\.[a-z0-9]+)?)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DiskFormatPattern();

    [GeneratedRegex(@"\bgit\s+.*push\b.*(--force\b|-f\b|\+[a-zA-Z0-9_\/]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitPushForcePattern();

    [GeneratedRegex(@"\bgit\s+.*reset\s+--hard\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitResetHardPattern();

    [GeneratedRegex(@"\bgit\s+.*clean\b.*-(?:[a-z]*f[a-z]*)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GitCleanForcePattern();

    [GeneratedRegex(@"\b(curl|wget)\b.*\|\s*(sh|bash|zsh|powershell|pwsh)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex CurlPipeShellPattern();

    [GeneratedRegex(@"\b(iwr|Invoke-WebRequest|curl|wget)\b.*\|\s*(iex|Invoke-Expression)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DownloadPipeIexPattern();

    [GeneratedRegex(@"\b(iex|Invoke-Expression)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex InvokeExpressionPattern();

    [GeneratedRegex(@"\breg\s+(add|delete|restore|import)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegModifyPattern();

    [GeneratedRegex(@"\b(Remove-ItemProperty|Set-ItemProperty|New-ItemProperty)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex RegistryCmdletPattern();

    [GeneratedRegex(@"\b(sudo|runas|shutdown)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ElevationAndShutdownPattern();

    [GeneratedRegex(@"\bSet-ExecutionPolicy\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SetExecutionPolicyPattern();

    // Medium risk tokens
    private static readonly char[] MediumRiskChars = [';', '|', '>', '`'];

    /// <summary>
    /// Classifies an <see cref="ApprovalRequest"/> by assessing both tool name and command line.
    /// </summary>
    public static RiskLevel Classify(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return Classify(request.ToolName, request.CommandLine);
    }

    /// <summary>
    /// Classifies a tool call given the tool name and command line text.
    /// </summary>
    public static RiskLevel Classify(string? toolName, string? commandLine)
    {
        var normalizedTool = toolName?.Trim().ToLowerInvariant() ?? string.Empty;
        var normalizedCommand = commandLine?.Trim() ?? string.Empty;

        // Check HIGH risk patterns first (zero false Low/Medium for High risk)
        if (!string.IsNullOrEmpty(normalizedCommand))
        {
            if (IsHighRiskCommand(normalizedCommand))
            {
                return RiskLevel.High;
            }
        }

        // Check MEDIUM risk
        // 1. File write tools
        if (FileWriteTools.Contains(normalizedTool))
        {
            return RiskLevel.Medium;
        }

        // 2. Chained commands, pipes, redirections, subshells
        if (!string.IsNullOrEmpty(normalizedCommand) && IsMediumRiskCommand(normalizedCommand))
        {
            return RiskLevel.Medium;
        }

        // Default: LOW risk
        return RiskLevel.Low;
    }

    private static bool IsHighRiskCommand(string cmd)
    {
        if (RmRfPattern().IsMatch(cmd)) return true;
        if (RmSeparateRfPattern().IsMatch(cmd)) return true;
        if (RemoveItemRecursePattern().IsMatch(cmd)) return true;
        if (DelOrRdSlashSPattern().IsMatch(cmd)) return true;
        if (DiskFormatPattern().IsMatch(cmd)) return true;
        if (GitPushForcePattern().IsMatch(cmd)) return true;
        if (GitResetHardPattern().IsMatch(cmd)) return true;
        if (GitCleanForcePattern().IsMatch(cmd)) return true;
        if (CurlPipeShellPattern().IsMatch(cmd)) return true;
        if (DownloadPipeIexPattern().IsMatch(cmd)) return true;
        if (InvokeExpressionPattern().IsMatch(cmd)) return true;
        if (RegModifyPattern().IsMatch(cmd)) return true;
        if (RegistryCmdletPattern().IsMatch(cmd)) return true;
        if (ElevationAndShutdownPattern().IsMatch(cmd)) return true;
        if (SetExecutionPolicyPattern().IsMatch(cmd)) return true;

        return false;
    }

    private static bool IsMediumRiskCommand(string cmd)
    {
        // Chained operators or subshell syntax:
        // ;, &&, ||, |, >, $(, `
        if (cmd.IndexOfAny(MediumRiskChars) >= 0) return true;
        if (cmd.Contains("&&", StringComparison.Ordinal)) return true;
        if (cmd.Contains("||", StringComparison.Ordinal)) return true;
        if (cmd.Contains("$(", StringComparison.Ordinal)) return true;

        return false;
    }
}
