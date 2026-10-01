namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Prepares presentation view models and summaries from an <see cref="ApprovalRequest"/>.
/// Formats readable descriptions, computes truncation (200-char limit), and determines
/// if expanded manual review is strictly required (<see cref="RequiresExpandedReview"/>).
/// </summary>
public sealed record ApprovalPresentation
{
    public const int MaxCollapsedLength = 200;
    public const int MaxOptionLabelLength = 60;

    public string ToolDisplayName { get; init; }
    public string ProjectFolder { get; init; }
    public string FullSummary { get; init; }
    public string CollapsedSummary { get; init; }
    public bool IsTruncated { get; init; }
    public RiskLevel Risk { get; init; }
    public bool RequiresExpandedReview { get; init; }
    public string OptionActionSummary { get; init; }

    private ApprovalPresentation(
        string toolDisplayName,
        string projectFolder,
        string fullSummary,
        string collapsedSummary,
        bool isTruncated,
        RiskLevel risk,
        bool requiresExpandedReview,
        string optionActionSummary)
    {
        ToolDisplayName = toolDisplayName;
        ProjectFolder = projectFolder;
        FullSummary = fullSummary;
        CollapsedSummary = collapsedSummary;
        IsTruncated = isTruncated;
        Risk = risk;
        RequiresExpandedReview = requiresExpandedReview;
        OptionActionSummary = optionActionSummary;
    }

    /// <summary>
    /// Builds an <see cref="ApprovalPresentation"/> from an <see cref="ApprovalRequest"/>.
    /// </summary>
    public static ApprovalPresentation Create(ApprovalRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        var risk = CommandRiskClassifier.Classify(request);
        string projectFolder = ResolveProjectFolder(request);
        string toolDisplayName = ResolveToolDisplayName(request.ToolName);
        string fullSummary = FormatFullSummary(request, projectFolder);
        string optionSummary = FormatOptionSummary(request);

        bool isTruncated = fullSummary.Length > MaxCollapsedLength;
        string collapsedSummary;
        if (isTruncated)
        {
            int extraChars = fullSummary.Length - MaxCollapsedLength;
            collapsedSummary = $"{fullSummary[..MaxCollapsedLength]} (+{extraChars} caracteres)";
        }
        else
        {
            collapsedSummary = fullSummary;
        }

        bool requiresExpandedReview = isTruncated || risk == RiskLevel.High;

        return new ApprovalPresentation(
            toolDisplayName: toolDisplayName,
            projectFolder: projectFolder,
            fullSummary: fullSummary,
            collapsedSummary: collapsedSummary,
            isTruncated: isTruncated,
            risk: risk,
            requiresExpandedReview: requiresExpandedReview,
            optionActionSummary: optionSummary);
    }

    private static string ResolveProjectFolder(ApprovalRequest request)
    {
        if (request.WorkspacePaths.Count > 0 && !string.IsNullOrWhiteSpace(request.WorkspacePaths[0]))
        {
            try
            {
                var dirName = Path.GetFileName(request.WorkspacePaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(dirName))
                {
                    return dirName;
                }
            }
            catch
            {
                // Fallback if path parsing fails
            }
        }

        if (!string.IsNullOrWhiteSpace(request.Cwd))
        {
            try
            {
                var dirName = Path.GetFileName(request.Cwd.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                if (!string.IsNullOrWhiteSpace(dirName))
                {
                    return dirName;
                }
            }
            catch
            {
                // Fallback
            }
        }

        return "Workspace";
    }

    private static string ResolveToolDisplayName(string toolName)
    {
        var normalized = toolName.ToLowerInvariant();
        return normalized switch
        {
            "run_command" => "Comando de terminal",
            "write_to_file" => "Crear archivo",
            "replace_file_content" or "multi_replace_file_content" => "Editar archivo",
            _ => toolName
        };
    }

    private static string FormatFullSummary(ApprovalRequest request, string projectFolder)
    {
        var tool = request.ToolName.ToLowerInvariant();
        switch (tool)
        {
            case "run_command":
                var cmd = request.CommandLine ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(request.Cwd))
                {
                    return $"{cmd} (en {request.Cwd})";
                }
                return cmd;

            case "write_to_file":
                var targetFile = MakeRelativePath(request.TargetFile, request.WorkspacePaths);
                int lineCount = CountLines(request.CodeContent);
                string linesDesc = lineCount == 1 ? "1 línea" : $"{lineCount} líneas";
                return $"Crear {targetFile} ({linesDesc})";

            case "replace_file_content" or "multi_replace_file_content":
                var editFile = MakeRelativePath(request.TargetFile, request.WorkspacePaths);
                return $"Editar {editFile}";

            default:
                return !string.IsNullOrWhiteSpace(request.CommandLine)
                    ? request.CommandLine
                    : request.ToolName;
        }
    }

    private static string FormatOptionSummary(ApprovalRequest request)
    {
        var tool = request.ToolName.ToLowerInvariant();
        string raw = tool switch
        {
            "run_command" => request.CommandLine ?? "comando",
            "write_to_file" => $"Crear {MakeRelativePath(request.TargetFile, request.WorkspacePaths)}",
            "replace_file_content" or "multi_replace_file_content" => $"Editar {MakeRelativePath(request.TargetFile, request.WorkspacePaths)}",
            _ => request.ToolName
        };

        raw = raw.Trim();
        if (raw.Length <= MaxOptionLabelLength)
        {
            return raw;
        }

        return $"{raw[..(MaxOptionLabelLength - 3)]}...";
    }

    private static string MakeRelativePath(string? path, IReadOnlyList<string> workspaces)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "archivo";
        }

        foreach (var ws in workspaces)
        {
            if (!string.IsNullOrWhiteSpace(ws) && path.StartsWith(ws, StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    return Path.GetRelativePath(ws, path);
                }
                catch
                {
                    // Fallback to filename
                }
            }
        }

        try
        {
            return Path.GetFileName(path);
        }
        catch
        {
            return path;
        }
    }

    private static int CountLines(string? content)
    {
        if (string.IsNullOrEmpty(content)) return 0;
        int count = 1;
        for (int i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n') count++;
        }
        return count;
    }
}
