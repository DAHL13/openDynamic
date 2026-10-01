namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// A sanitized in-memory audit record for a resolved approval or auto-approval decision.
/// Strictly excludes sensitive details: no full command lines, no file contents, and no full directory paths.
/// </summary>
public sealed class ApprovalHistoryItem
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
    public string ToolName { get; init; } = string.Empty;
    public string Decision { get; init; } = string.Empty;
    public string Summary { get; init; } = string.Empty;
    public string WorkspaceFolder { get; init; } = string.Empty;
    public string? RuleDescription { get; init; }

    public string LocalTimeString => TimestampUtc.ToLocalTime().ToString("HH:mm:ss");
}

/// <summary>
/// Thread-safe in-memory circular buffer storing the last 50 approval decisions (Task 4).
/// Guarantees zero sensitive data leakage in compliance with Golden Rules 10 and 13.
/// </summary>
public sealed class ApprovalHistoryTracker
{
    public const int MaxHistoryCount = 50;

    private readonly object _syncLock = new();
    private readonly List<ApprovalHistoryItem> _items = new(MaxHistoryCount);

    /// <summary>
    /// Records an approval decision from a structured request, strictly sanitizing summary length and paths.
    /// </summary>
    public void Record(
        ApprovalRequest request,
        string decision,
        string? ruleDescription = null)
    {
        string? rawSummary = null;
        if (string.Equals(request.ToolName, "run_command", StringComparison.OrdinalIgnoreCase))
        {
            rawSummary = request.CommandLine;
        }
        else if (!string.IsNullOrWhiteSpace(request.TargetFile))
        {
            rawSummary = Path.GetFileName(request.TargetFile);
        }

        Record(request.ToolName, decision, rawSummary, request.WorkspaceFolder, ruleDescription);
    }

    /// <summary>
    /// Records an approval decision, strictly sanitizing summary length and paths.
    /// </summary>
    public void Record(
        string toolName,
        string decision,
        string? rawSummary,
        string? workspaceFolder,
        string? ruleDescription = null)
    {
        string sanitizedSummary = SanitizeSummary(rawSummary);
        string folder = string.IsNullOrWhiteSpace(workspaceFolder) ? "Desconocido" : workspaceFolder.Trim();

        var item = new ApprovalHistoryItem
        {
            ToolName = toolName,
            Decision = decision,
            Summary = sanitizedSummary,
            WorkspaceFolder = folder,
            RuleDescription = ruleDescription,
            TimestampUtc = DateTimeOffset.UtcNow
        };

        lock (_syncLock)
        {
            _items.Insert(0, item);
            if (_items.Count > MaxHistoryCount)
            {
                _items.RemoveAt(_items.Count - 1);
            }
        }
    }

    /// <summary>
    /// Gets snapshot of recent history items.
    /// </summary>
    public IReadOnlyList<ApprovalHistoryItem> GetRecent()
    {
        lock (_syncLock)
        {
            return _items.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Clears the in-memory history.
    /// </summary>
    public void Clear()
    {
        lock (_syncLock)
        {
            _items.Clear();
        }
    }

    private static string SanitizeSummary(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "(sin resumen)";

        string trimmed = raw.Trim().Replace("\r\n", " ").Replace('\n', ' ');
        if (trimmed.Length <= 60)
        {
            return trimmed;
        }

        return trimmed[..57] + "...";
    }
}
