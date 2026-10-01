namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Validates commands against safe prefix criteria for optional prefix-based project authorization (Task 6b).
/// Ensures strict token boundaries, benign-only arguments, and complete exclusion of shells, downloaders, and chained commands.
/// </summary>
public static class SafePrefixMatcher
{
    private static readonly HashSet<string> ExcludedCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "powershell", "pwsh", "cmd", "cmd.exe", "bash", "sh", "zsh", "wsl",
        "python", "python3", "node", "ruby", "perl",
        "curl", "wget", "bitsadmin", "certutil", "irm", "iex"
    };

    public static readonly IReadOnlyList<string> DefaultWhitelist = new[]
    {
        "git status",
        "git diff",
        "git log",
        "git show",
        "dotnet build",
        "dotnet test",
        "dotnet restore",
        "ls",
        "dir"
    };

    /// <summary>
    /// Evaluates if a command is a candidate for creating a safe prefix rule.
    /// </summary>
    public static (bool IsCandidate, string? MatchedPrefix) EvaluateCandidate(
        string? rawCommand,
        IEnumerable<string>? whitelist = null,
        RiskLevel risk = RiskLevel.Low)
    {
        if (string.IsNullOrWhiteSpace(rawCommand) || risk == RiskLevel.High)
        {
            return (false, null);
        }

        string cmd = rawCommand.Trim();

        // 1. Exclude forbidden shells and downloaders
        if (StartsWithExcludedCommand(cmd))
        {
            return (false, null);
        }

        var prefixes = whitelist ?? DefaultWhitelist;

        // Sort prefixes by length descending to match most specific prefix first
        foreach (var prefix in prefixes.OrderByDescending(p => p.Length))
        {
            string pTrim = prefix.Trim();
            if (string.IsNullOrEmpty(pTrim)) continue;

            if (MatchesPrefixToken(cmd, pTrim))
            {
                string remainder = cmd[pTrim.Length..];
                if (IsRemainderBenign(remainder))
                {
                    return (true, pTrim);
                }
            }
        }

        return (false, null);
    }

    /// <summary>
    /// Checks whether an incoming command satisfies an existing safe prefix rule.
    /// </summary>
    public static bool MatchesRule(string? incomingCommand, string prefixPattern, RiskLevel risk = RiskLevel.Low)
    {
        if (string.IsNullOrWhiteSpace(incomingCommand) || string.IsNullOrWhiteSpace(prefixPattern))
        {
            return false;
        }

        if (risk == RiskLevel.High)
        {
            return false;
        }

        string cmd = incomingCommand.Trim();
        string prefix = prefixPattern.Trim();

        if (StartsWithExcludedCommand(cmd))
        {
            return false;
        }

        if (!MatchesPrefixToken(cmd, prefix))
        {
            return false;
        }

        string remainder = cmd[prefix.Length..];
        return IsRemainderBenign(remainder);
    }

    private static bool MatchesPrefixToken(string command, string prefix)
    {
        if (!command.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Must end at exact length or be followed by whitespace
        if (command.Length == prefix.Length)
        {
            return true;
        }

        return char.IsWhiteSpace(command[prefix.Length]);
    }

    private static bool StartsWithExcludedCommand(string command)
    {
        // Extract first token
        int spaceIdx = command.IndexOf(' ');
        string firstToken = spaceIdx > 0 ? command[..spaceIdx] : command;
        firstToken = firstToken.Trim().Trim('\"', '\'');

        return ExcludedCommands.Contains(firstToken);
    }

    private static bool IsRemainderBenign(string remainder)
    {
        if (string.IsNullOrEmpty(remainder))
        {
            return true;
        }

        for (int i = 0; i < remainder.Length; i++)
        {
            char c = remainder[i];

            // Explicit forbidden characters per task 6b specification
            if (c is ';' or '&' or '|' or '>' or '<' or '`' or '$' or '\r' or '\n' or '(' or ')')
            {
                return false;
            }

            // Benign characters: letters, numbers, spaces, and . _ - : / \ = , " '
            bool isBenign = char.IsLetterOrDigit(c) ||
                            char.IsWhiteSpace(c) ||
                            c is '.' or '_' or '-' or ':' or '/' or '\\' or '=' or ',' or '\"' or '\'';

            if (!isBenign)
            {
                return false;
            }
        }

        return true;
    }
}
