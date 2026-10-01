using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace OpenDynamic.Core.AgentApprovals;

/// <summary>
/// Thread-safe persistence store and in-memory cache for approval rules (Task 2).
/// Manages RAM-only conversation rules, atomic JSON persistence for project (.antigravity/approval-rules.json)
/// and global rules (%USERPROFILE%/.antigravity/approval-rules.json), automatic .bak recovery,
/// and a maximum capacity of 200 rules per scope.
/// </summary>
public sealed class ApprovalRuleStore
{
    public const int MaxRulesPerScope = 200;
    public const string RulesFileName = "approval-rules.json";
    public const string RulesFolderName = ".antigravity";

    private readonly object _syncLock = new();
    private readonly List<ApprovalRule> _conversationRules = new();
    private readonly List<ApprovalRule> _projectRules = new();
    private readonly List<ApprovalRule> _globalRules = new();
    private readonly string _globalRulesFilePath;
    private readonly Action<string>? _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter<ApprovalRuleScope>() }
    };

    public ApprovalRuleStore(string? globalRulesFilePath = null, Action<string>? logger = null)
    {
        _logger = logger;
        _globalRulesFilePath = globalRulesFilePath ?? GetDefaultGlobalRulesFilePath();

        LoadGlobalRulesSafe();
    }

    public static string GetDefaultGlobalRulesFilePath()
    {
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(userProfile, RulesFolderName, RulesFileName);
    }

    public static string GetProjectRulesFilePath(string workspaceRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workspaceRoot);
        return Path.Combine(workspaceRoot, RulesFolderName, RulesFileName);
    }

    /// <summary>
    /// Finds a rule matching the incoming approval request, or null if none matches.
    /// </summary>
    public ApprovalRule? FindMatchingRule(ApprovalRequest request, RiskLevel risk = RiskLevel.Low, bool enableSafePrefix = false)
    {
        if (request == null || risk == RiskLevel.High) return null;
        if (!string.Equals(request.ToolName, "run_command", StringComparison.OrdinalIgnoreCase)) return null;

        lock (_syncLock)
        {
            // 1. Check conversation rules (RAM)
            foreach (var rule in _conversationRules)
            {
                if (ApprovalRuleMatcher.Matches(rule, request, risk))
                {
                    return rule;
                }
            }

            // 2. Check project rules
            foreach (var rule in _projectRules)
            {
                if (!enableSafePrefix && rule.IsPrefixMatch) continue;

                if (ApprovalRuleMatcher.Matches(rule, request, risk))
                {
                    return rule;
                }
            }

            // 3. Check global rules
            foreach (var rule in _globalRules)
            {
                if (!enableSafePrefix && rule.IsPrefixMatch) continue;

                if (ApprovalRuleMatcher.Matches(rule, request, risk))
                {
                    return rule;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Adds a new approval rule and persists it according to its scope.
    /// </summary>
    public void AddRule(ApprovalRule rule, string? workspaceRoot = null)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_syncLock)
        {
            switch (rule.Scope)
            {
                case ApprovalRuleScope.Conversation:
                    _conversationRules.RemoveAll(r =>
                        string.Equals(r.ConversationId, rule.ConversationId, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.CommandPattern, rule.CommandPattern, StringComparison.Ordinal));
                    _conversationRules.Insert(0, rule);
                    PruneList(_conversationRules);
                    break;

                case ApprovalRuleScope.Project:
                    string ruleWs = ApprovalRuleMatcher.NormalizePath(rule.WorkspaceKey ?? workspaceRoot ?? string.Empty);
                    _projectRules.RemoveAll(r =>
                        string.Equals(ApprovalRuleMatcher.NormalizePath(r.WorkspaceKey), ruleWs, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(r.CommandPattern, rule.CommandPattern, StringComparison.Ordinal) &&
                        r.IsPrefixMatch == rule.IsPrefixMatch);
                    _projectRules.Insert(0, rule);
                    PruneList(_projectRules);

                    if (!string.IsNullOrWhiteSpace(workspaceRoot))
                    {
                        SaveProjectRulesSafe(workspaceRoot);
                    }
                    break;

                case ApprovalRuleScope.Global:
                    _globalRules.RemoveAll(r =>
                        string.Equals(r.CommandPattern, rule.CommandPattern, StringComparison.Ordinal) &&
                        r.IsPrefixMatch == rule.IsPrefixMatch);
                    _globalRules.Insert(0, rule);
                    PruneList(_globalRules);
                    SaveGlobalRulesSafe();
                    break;
            }
        }
    }

    /// <summary>
    /// Removes a rule by its ID.
    /// </summary>
    public bool RemoveRule(string ruleId, string? workspaceRoot = null)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return false;

        lock (_syncLock)
        {
            int removed = _conversationRules.RemoveAll(r => r.Id == ruleId);
            if (removed > 0) return true;

            removed = _projectRules.RemoveAll(r => r.Id == ruleId);
            if (removed > 0)
            {
                if (!string.IsNullOrWhiteSpace(workspaceRoot))
                {
                    SaveProjectRulesSafe(workspaceRoot);
                }
                return true;
            }

            removed = _globalRules.RemoveAll(r => r.Id == ruleId);
            if (removed > 0)
            {
                SaveGlobalRulesSafe();
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Clears rules for a given scope, or all rules if null.
    /// </summary>
    public void ClearRules(ApprovalRuleScope? scope = null, string? workspaceRoot = null)
    {
        lock (_syncLock)
        {
            if (scope is null or ApprovalRuleScope.Conversation)
            {
                _conversationRules.Clear();
            }

            if (scope is null or ApprovalRuleScope.Project)
            {
                _projectRules.Clear();
                if (!string.IsNullOrWhiteSpace(workspaceRoot))
                {
                    SaveProjectRulesSafe(workspaceRoot);
                }
            }

            if (scope is null or ApprovalRuleScope.Global)
            {
                _globalRules.Clear();
                SaveGlobalRulesSafe();
            }
        }
    }

    /// <summary>
    /// Records usage timestamp and counter for a rule.
    /// </summary>
    public void RecordUsage(string ruleId, string? workspaceRoot = null)
    {
        if (string.IsNullOrWhiteSpace(ruleId)) return;

        lock (_syncLock)
        {
            var rule = _conversationRules.FirstOrDefault(r => r.Id == ruleId) ??
                       _projectRules.FirstOrDefault(r => r.Id == ruleId) ??
                       _globalRules.FirstOrDefault(r => r.Id == ruleId);

            if (rule != null)
            {
                rule.LastUsedAtUtc = DateTimeOffset.UtcNow;
                rule.UseCount++;

                if (rule.Scope == ApprovalRuleScope.Global)
                {
                    SaveGlobalRulesSafe();
                }
                else if (rule.Scope == ApprovalRuleScope.Project && !string.IsNullOrWhiteSpace(workspaceRoot))
                {
                    SaveProjectRulesSafe(workspaceRoot);
                }
            }
        }
    }

    /// <summary>
    /// Gets all active rules across all scopes.
    /// </summary>
    public IReadOnlyList<ApprovalRule> GetAllRules()
    {
        lock (_syncLock)
        {
            var all = new List<ApprovalRule>(_conversationRules.Count + _projectRules.Count + _globalRules.Count);
            all.AddRange(_conversationRules);
            all.AddRange(_projectRules);
            all.AddRange(_globalRules);
            return all.AsReadOnly();
        }
    }

    /// <summary>
    /// Gets rules filtered by scope.
    /// </summary>
    public IReadOnlyList<ApprovalRule> GetRules(ApprovalRuleScope scope)
    {
        lock (_syncLock)
        {
            return scope switch
            {
                ApprovalRuleScope.Conversation => _conversationRules.ToList().AsReadOnly(),
                ApprovalRuleScope.Project => _projectRules.ToList().AsReadOnly(),
                ApprovalRuleScope.Global => _globalRules.ToList().AsReadOnly(),
                _ => Array.Empty<ApprovalRule>()
            };
        }
    }

    /// <summary>
    /// Loads project rules from a specific workspace folder into memory.
    /// </summary>
    public void LoadProjectRules(string workspaceRoot)
    {
        if (string.IsNullOrWhiteSpace(workspaceRoot)) return;

        string normWorkspace = ApprovalRuleMatcher.NormalizePath(workspaceRoot);
        string path = GetProjectRulesFilePath(workspaceRoot);
        var loaded = LoadRulesFromDiskSafe(path);

        foreach (var rule in loaded)
        {
            if (string.IsNullOrWhiteSpace(rule.WorkspaceKey))
            {
                rule.WorkspaceKey = normWorkspace;
            }
        }

        lock (_syncLock)
        {
            _projectRules.RemoveAll(r => string.Equals(ApprovalRuleMatcher.NormalizePath(r.WorkspaceKey), normWorkspace, StringComparison.OrdinalIgnoreCase));
            _projectRules.AddRange(loaded);
            PruneList(_projectRules);
        }
    }

    private void LoadGlobalRulesSafe()
    {
        var loaded = LoadRulesFromDiskSafe(_globalRulesFilePath);
        lock (_syncLock)
        {
            _globalRules.Clear();
            _globalRules.AddRange(loaded);
            PruneList(_globalRules);
        }
    }

    private void SaveGlobalRulesSafe()
    {
        List<ApprovalRule> copy;
        lock (_syncLock)
        {
            copy = _globalRules.ToList();
        }

        SaveRulesToDiskSafe(_globalRulesFilePath, copy);
    }

    private void SaveProjectRulesSafe(string workspaceRoot)
    {
        string normWorkspace = ApprovalRuleMatcher.NormalizePath(workspaceRoot);
        List<ApprovalRule> copy;
        lock (_syncLock)
        {
            copy = _projectRules.Where(r => string.Equals(ApprovalRuleMatcher.NormalizePath(r.WorkspaceKey), normWorkspace, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        string path = GetProjectRulesFilePath(workspaceRoot);
        SaveRulesToDiskSafe(path, copy);
    }

    private List<ApprovalRule> LoadRulesFromDiskSafe(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return new List<ApprovalRule>();
        }

        try
        {
            return ReadJsonFile(filePath);
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Warning: Failed to parse rules file at {filePath}: {ex.Message}. Attempting backup recovery.");

            string bakPath = filePath + ".bak";
            if (File.Exists(bakPath))
            {
                try
                {
                    var recovered = ReadJsonFile(bakPath);
                    _logger?.Invoke($"Successfully recovered {recovered.Count} rules from backup {bakPath}");
                    return recovered;
                }
                catch (Exception bakEx)
                {
                    _logger?.Invoke($"Warning: Backup rules file at {bakPath} also corrupted: {bakEx.Message}");
                }
            }

            return new List<ApprovalRule>();
        }
    }

    private static List<ApprovalRule> ReadJsonFile(string filePath)
    {
        string json = File.ReadAllText(filePath);
        var doc = JsonSerializer.Deserialize<RulesDocument>(json, JsonOptions);
        return doc?.Rules ?? new List<ApprovalRule>();
    }

    private void SaveRulesToDiskSafe(string filePath, List<ApprovalRule> rules)
    {
        try
        {
            string? dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var doc = new RulesDocument
            {
                SchemaVersion = 1,
                UpdatedAtUtc = DateTimeOffset.UtcNow,
                Rules = rules
            };

            string json = JsonSerializer.Serialize(doc, JsonOptions);
            string tmpPath = filePath + ".tmp";
            string bakPath = filePath + ".bak";

            File.WriteAllText(tmpPath, json);

            // Atomic replace with backup
            if (File.Exists(filePath))
            {
                File.Copy(filePath, bakPath, overwrite: true);
                File.Move(tmpPath, filePath, overwrite: true);
            }
            else
            {
                File.Move(tmpPath, filePath);
            }
        }
        catch (Exception ex)
        {
            _logger?.Invoke($"Error saving approval rules to {filePath}: {ex.Message}");
        }
    }

    private static void PruneList(List<ApprovalRule> list)
    {
        if (list.Count > MaxRulesPerScope)
        {
            // Keep most recently used
            var pruned = list.OrderByDescending(r => r.LastUsedAtUtc).Take(MaxRulesPerScope).ToList();
            list.Clear();
            list.AddRange(pruned);
        }
    }

    private sealed class RulesDocument
    {
        public int SchemaVersion { get; set; } = 1;
        public DateTimeOffset UpdatedAtUtc { get; set; } = DateTimeOffset.UtcNow;
        public List<ApprovalRule> Rules { get; set; } = new();
    }
}
