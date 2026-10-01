using System.Collections.ObjectModel;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Native;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.AgentApprovals.Views;
using OpenDynamic.App.Widgets.Messages;
using OpenDynamic.Core.AgentApprovals;
using OpenDynamic.Core.Hotkeys;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.AgentApprovals;

/// <summary>
/// Notch Dynamic Island widget for Antigravity permission requests (Priority 95).
/// Features 5 structured numbered options matching Antigravity native dialog,
/// FIFO multi-request queue, risk badges, safe prefix opt-in, anti-accidental click guard,
/// and dynamic keyboard hotkeys (Ctrl+Alt+1..5, Enter, A) (Golden Rules 12 &amp; 13).
/// </summary>
public sealed class ApprovalWidget : IslandWidgetBase
{
    public const string WidgetId = "AgentApprovalWidget";
    public const int HotkeyOption1Id = 0xA001;
    public const int HotkeyOption2Id = 0xA002;
    public const int HotkeyOption3Id = 0xA003;
    public const int HotkeyOption4Id = 0xA004;
    public const int HotkeyOption5Id = 0xA005;
    public const int HotkeyEnterId = 0xA00E;
    public const int HotkeyDecideAgyId = 0xA00A;

    private readonly AppSettings _settings;
    private readonly ApprovalRuleStore _ruleStore;
    private readonly Dispatcher _dispatcher;

    private readonly List<ApprovalQueueItem> _queue = new();
    private ApprovalSessionPolicy? _activeSession;
    private DispatcherTimer? _countdownTimer;
    private DispatcherTimer? _graceTimer;
    private DispatcherTimer? _autoAllowNoticeTimer;

    private bool _isReviewConfirmed;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _areHotkeysRegistered;
    private string? _matchedSafePrefix;

    private string _toolDisplayName = string.Empty;
    private string _projectFolder = "Workspace";
    private string _fullSummary = string.Empty;
    private string _collapsedSummary = string.Empty;
    private string _optionActionSummary = string.Empty;
    private string _remainingSecondsText = string.Empty;
    private string _selectedDenyReason = string.Empty;
    private string _queuePositionText = string.Empty;
    private string _transientNoticeText = string.Empty;
    private bool _isShowingNotice;
    private RiskLevel _risk = RiskLevel.Low;

    public override string Id => WidgetId;

    public string ToolDisplayName
    {
        get => _toolDisplayName;
        private set => SetProperty(ref _toolDisplayName, value);
    }

    public string ProjectFolder
    {
        get => _projectFolder;
        private set => SetProperty(ref _projectFolder, value);
    }

    public string FullSummary
    {
        get => _fullSummary;
        private set => SetProperty(ref _fullSummary, value);
    }

    public string CollapsedSummary
    {
        get => _collapsedSummary;
        private set => SetProperty(ref _collapsedSummary, value);
    }

    public string OptionActionSummary
    {
        get => _optionActionSummary;
        private set => SetProperty(ref _optionActionSummary, value);
    }

    public string RemainingSecondsText
    {
        get => _remainingSecondsText;
        private set => SetProperty(ref _remainingSecondsText, value);
    }

    public string QueuePositionText
    {
        get => _queuePositionText;
        private set => SetProperty(ref _queuePositionText, value);
    }

    public Visibility QueueBadgeVisibility => _queue.Count > 1
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SatelliteQueueText => _queue.Count > 1
        ? $"+{_queue.Count - 1} en cola"
        : string.Empty;

    public string TransientNoticeText
    {
        get => _transientNoticeText;
        private set => SetProperty(ref _transientNoticeText, value);
    }

    public bool IsShowingNotice
    {
        get => _isShowingNotice;
        private set => SetProperty(ref _isShowingNotice, value);
    }

    public RiskLevel Risk
    {
        get => _risk;
        private set
        {
            if (SetProperty(ref _risk, value))
            {
                OnPropertyChanged(nameof(RiskColorBrush));
                OnPropertyChanged(nameof(RiskBackgroundBrush));
                OnPropertyChanged(nameof(RiskBadgeText));
                OnPropertyChanged(nameof(HotkeyGuideText));
                OnPropertyChanged(nameof(AreAlwaysOptionsVisible));
                OnPropertyChanged(nameof(AlwaysOptionsVisibility));
                OnPropertyChanged(nameof(IsSafePrefixOptionVisible));
                OnPropertyChanged(nameof(SafePrefixOptionVisibility));
            }
        }
    }

    public SolidColorBrush RiskColorBrush => Risk switch
    {
        RiskLevel.High => new SolidColorBrush(Color.FromRgb(0xFF, 0x45, 0x3A)), // Red
        RiskLevel.Medium => new SolidColorBrush(Color.FromRgb(0xFF, 0x9F, 0x0A)), // Amber
        _ => new SolidColorBrush(Color.FromRgb(0x34, 0xC7, 0x59)) // Green
    };

    public SolidColorBrush RiskBackgroundBrush => Risk switch
    {
        RiskLevel.High => new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x45, 0x3A)),
        RiskLevel.Medium => new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0x9F, 0x0A)),
        _ => new SolidColorBrush(Color.FromArgb(0x33, 0x34, 0xC7, 0x59))
    };

    public string RiskBadgeText => Risk switch
    {
        RiskLevel.High => "Riesgo Alto",
        RiskLevel.Medium => "Riesgo Medio",
        _ => "Riesgo Bajo"
    };

    public bool CanAllow => _activeSession?.CanAllow() == true;
    public bool CanDeny => _activeSession?.CanDeny() == true;

    /// <summary>
    /// Security Restriction (Rule 12): Options 2, 3, and 4 are strictly hidden
    /// for High risk commands or file modification tools.
    /// </summary>
    public bool AreAlwaysOptionsVisible =>
        _activeSession != null &&
        string.Equals(_activeSession.Request.ToolName, "run_command", StringComparison.OrdinalIgnoreCase) &&
        Risk != RiskLevel.High &&
        !_activeSession.Presentation.RequiresExpandedReview;

    public Visibility AlwaysOptionsVisibility => AreAlwaysOptionsVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public bool IsSafePrefixOptionVisible =>
        _settings.EnableAgentSafePrefixRules &&
        AreAlwaysOptionsVisible &&
        !string.IsNullOrWhiteSpace(_matchedSafePrefix);

    public Visibility SafePrefixOptionVisibility => IsSafePrefixOptionVisible
        ? Visibility.Visible
        : Visibility.Collapsed;

    public string SafePrefixOptionLabel => $"3b. Sí, y permitir '{_matchedSafePrefix} …' en este proyecto";

    public string AllowButtonLabel
    {
        get
        {
            if (_activeSession?.IsInGracePeriod() == true)
            {
                return $"1. Sí, permitir esta vez - (Espera...)";
            }
            if (_activeSession?.Presentation.RequiresExpandedReview == true && !_isReviewConfirmed)
            {
                return $"1. Sí, permitir esta vez - (Revisión requerida)";
            }
            return $"1. Sí, permitir esta vez";
        }
    }

    public string Option2ButtonLabel => "2. Sí, y siempre en esta conversación";
    public string Option3ButtonLabel => "3. Sí, y siempre en este proyecto";
    public string Option4ButtonLabel => "4. Sí, y siempre globalmente";

    public SolidColorBrush AllowButtonForeground => CanAllow
        ? new SolidColorBrush(Color.FromRgb(0x30, 0xD1, 0x58))
        : new SolidColorBrush(Color.FromRgb(0x63, 0x63, 0x66));

    public string GracePeriodIndicator => _activeSession?.IsInGracePeriod() == true
        ? $"(guarda activa {Math.Ceiling(_activeSession.GetRemainingGracePeriod().TotalMilliseconds)} ms)"
        : string.Empty;

    public Visibility ReviewRequiredBannerVisibility =>
        (_activeSession?.Presentation.RequiresExpandedReview == true && !_isReviewConfirmed)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public string ReviewBannerText => Risk == RiskLevel.High
        ? "⚠️ Comando de alto riesgo: examine el comando completo antes de permitir."
        : "ℹ️ Comando extenso truncado: confirme revisión para habilitar aprobación.";

    public Visibility ConfirmReviewButtonVisibility =>
        (_activeSession?.Presentation.RequiresExpandedReview == true && !_isReviewConfirmed)
            ? Visibility.Visible
            : Visibility.Collapsed;

    public Thickness Option1BorderThickness => _settings.AgentApprovalHighlightedOption == 1
        ? new Thickness(1.5)
        : new Thickness(0);

    public SolidColorBrush Option1BorderBrush => new(Color.FromRgb(0x30, 0xD1, 0x58));

    public Thickness Option2BorderThickness => _settings.AgentApprovalHighlightedOption == 2
        ? new Thickness(1.5)
        : new Thickness(0);

    public SolidColorBrush Option2BorderBrush => new(Color.FromRgb(0x30, 0xD1, 0x58));

    public Thickness Option3BorderThickness => _settings.AgentApprovalHighlightedOption == 3
        ? new Thickness(1.5)
        : new Thickness(0);

    public SolidColorBrush Option3BorderBrush => new(Color.FromRgb(0x30, 0xD1, 0x58));

    public Thickness Option4BorderThickness => _settings.AgentApprovalHighlightedOption == 4
        ? new Thickness(1.5)
        : new Thickness(0);

    public SolidColorBrush Option4BorderBrush => new(Color.FromRgb(0x30, 0xD1, 0x58));

    public Thickness Option5BorderThickness => _settings.AgentApprovalHighlightedOption == 5
        ? new Thickness(1.5)
        : new Thickness(0);

    public SolidColorBrush Option5BorderBrush => new(Color.FromRgb(0xFF, 0x45, 0x3A));

    public ObservableCollection<string> PredefinedDenyReasons { get; } = new();

    public string SelectedDenyReason
    {
        get => _selectedDenyReason;
        set => SetProperty(ref _selectedDenyReason, value);
    }

    public string HotkeyGuideText
    {
        get
        {
            if (Risk == RiskLevel.High)
            {
                return "Atajos: Ctrl+Alt+1 [Bloqueado por riesgo] • Ctrl+Alt+5 (Denegar) • Ctrl+Alt+A (Nativo)";
            }
            if (AreAlwaysOptionsVisible)
            {
                return "Atajos: Ctrl+Alt+1 a 5 • Ctrl+Alt+Enter (Resaltada) • Ctrl+Alt+A (Nativo)";
            }
            return "Atajos: Ctrl+Alt+1 (Permitir) • Ctrl+Alt+5 (Denegar) • Ctrl+Alt+A (Nativo)";
        }
    }

    public ICommand AllowCommand { get; }
    public ICommand AllowAlwaysConversationCommand { get; }
    public ICommand AllowAlwaysProjectCommand { get; }
    public ICommand AllowSafePrefixProjectCommand { get; }
    public ICommand AllowAlwaysGlobalCommand { get; }
    public ICommand DenyCommand { get; }
    public ICommand DecideInAntigravityCommand { get; }
    public ICommand ConfirmReviewCommand { get; }

    public ApprovalWidget(
        AppSettings settings,
        ApprovalRuleStore? ruleStore = null,
        Dispatcher? dispatcher = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _ruleStore = ruleStore ?? new ApprovalRuleStore();
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        Priority = ActivityPriority.AgentApproval;

        AllowCommand = new RelayCommand(ExecuteOption1);
        AllowAlwaysConversationCommand = new RelayCommand(ExecuteOption2);
        AllowAlwaysProjectCommand = new RelayCommand(ExecuteOption3);
        AllowSafePrefixProjectCommand = new RelayCommand(ExecuteOption3b);
        AllowAlwaysGlobalCommand = new RelayCommand(ExecuteOption4);
        DenyCommand = new RelayCommand(ExecuteOption5);
        DecideInAntigravityCommand = new RelayCommand(ExecuteDecideInAntigravity);
        ConfirmReviewCommand = new RelayCommand(ExecuteConfirmReview);

        LoadPredefinedReasons();
    }

    public void InitializeHwnd(IntPtr hwnd)
    {
        _hwnd = hwnd;
    }

    private void LoadPredefinedReasons()
    {
        PredefinedDenyReasons.Clear();
        foreach (var r in _settings.AgentApprovalPredefinedDenyReasons)
        {
            PredefinedDenyReasons.Add(r);
        }

        if (PredefinedDenyReasons.Count > 0)
        {
            SelectedDenyReason = PredefinedDenyReasons[0];
        }
    }

    public override void Initialize()
    {
        // Ready for requests
    }

    public override UserControl? CreateCompactView() => new ApprovalCompactView { DataContext = this };
    public override UserControl? CreateExpandedView() => new ApprovalExpandedView { DataContext = this };
    public override UserControl? CreateSplitView() => CreateCompactView();

    /// <summary>
    /// Handles an incoming approval session on the UI thread using a FIFO queue.
    /// Resolves when user answers or session times out.
    /// </summary>
    public Task<ApprovalResponse> HandleRequestAsync(ApprovalSessionPolicy session)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            var tcs = new TaskCompletionSource<ApprovalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            var queueItem = new ApprovalQueueItem(session, tcs);
            _queue.Add(queueItem);

            UpdateQueueProperties();

            if (_queue.Count == 1)
            {
                ActivateQueueItem(queueItem);
            }

            return tcs.Task;
        }).Task.Unwrap();
    }

    public void HandleCancelled(string requestId)
    {
        _dispatcher.InvokeAsync(() =>
        {
            int index = _queue.FindIndex(item => item.Session.Request.Id == requestId);
            if (index == 0)
            {
                // Active item disconnected
                CompleteResolution(_activeSession?.CancelByClientDisconnect() ?? ApprovalResponse.AskNative("Cliente desconectado"));
            }
            else if (index > 0)
            {
                // Queued item disconnected while waiting
                var item = _queue[index];
                _queue.RemoveAt(index);
                item.Tcs.TrySetResult(item.Session.CancelByClientDisconnect());
                UpdateQueueProperties();
            }
        });
    }

    /// <summary>
    /// Shows a transient 2-second note in the notch when a command was auto-approved by rule (Task 4).
    /// </summary>
    public void ShowAutoAllowedNotice(ApprovalRule rule, ApprovalRequest request)
    {
        _dispatcher.InvokeAsync(() =>
        {
            if (_queue.Count > 0)
            {
                // Do not interrupt active approval prompts
                return;
            }

            TransientNoticeText = $"Permitido por regla: {rule.DisplaySummary}";
            IsShowingNotice = true;
            IsActive = true;

            _autoAllowNoticeTimer?.Stop();
            _autoAllowNoticeTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = TimeSpan.FromSeconds(2)
            };
            _autoAllowNoticeTimer.Tick += (_, _) =>
            {
                _autoAllowNoticeTimer.Stop();
                _autoAllowNoticeTimer = null;
                IsShowingNotice = false;
                if (_queue.Count == 0)
                {
                    IsActive = false;
                }
            };
            _autoAllowNoticeTimer.Start();
        });
    }

    private void ActivateQueueItem(ApprovalQueueItem item)
    {
        _activeSession = item.Session;
        _isReviewConfirmed = !_activeSession.Presentation.RequiresExpandedReview;

        var pres = _activeSession.Presentation;
        ToolDisplayName = pres.ToolDisplayName;
        ProjectFolder = pres.ProjectFolder;
        FullSummary = pres.FullSummary;
        CollapsedSummary = pres.CollapsedSummary;
        OptionActionSummary = pres.OptionActionSummary;
        Risk = pres.Risk;

        // Check safe prefix candidate
        if (_settings.EnableAgentSafePrefixRules && Risk != RiskLevel.High)
        {
            var (isCandidate, matched) = SafePrefixMatcher.EvaluateCandidate(
                _activeSession.Request.CommandLine,
                _settings.AgentSafePrefixWhitelist,
                Risk);
            _matchedSafePrefix = isCandidate ? matched : null;
        }
        else
        {
            _matchedSafePrefix = null;
        }

        LoadPredefinedReasons();
        UpdateSessionProperties();
        UpdateQueueProperties();

        if (_settings.EnableAgentApprovalSound)
        {
            try
            {
                SystemSounds.Exclamation.Play();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to play exclamation sound for approval request.");
            }
        }

        IsActive = true;

        // Auto-expand into notch without stealing focus (WS_EX_NOACTIVATE)
        WeakReferenceMessenger.Default.Send(new ExpandRequestedMessage(WidgetId));

        // Start grace period timer
        _graceTimer?.Stop();
        var remainingGrace = _activeSession.GetRemainingGracePeriod();
        if (remainingGrace > TimeSpan.Zero)
        {
            _graceTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = remainingGrace
            };
            _graceTimer.Tick += (_, _) =>
            {
                _graceTimer.Stop();
                UpdateSessionProperties();
            };
            _graceTimer.Start();
        }

        // Start countdown timer (1s interval)
        _countdownTimer?.Stop();
        _countdownTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = TimeSpan.FromSeconds(1)
        };
        _countdownTimer.Tick += OnCountdownTick;
        _countdownTimer.Start();
        UpdateCountdownDisplay();

        RegisterDynamicHotkeys();
    }

    private void UpdateQueueProperties()
    {
        QueuePositionText = _queue.Count > 1 ? $"1 / {_queue.Count}" : string.Empty;
        OnPropertyChanged(nameof(QueuePositionText));
        OnPropertyChanged(nameof(QueueBadgeVisibility));
        OnPropertyChanged(nameof(SatelliteQueueText));
    }

    private void OnCountdownTick(object? sender, EventArgs e)
    {
        if (_activeSession == null)
        {
            _countdownTimer?.Stop();
            return;
        }

        if (_activeSession.HasTimedOut())
        {
            _countdownTimer?.Stop();
            var expResponse = _activeSession.Expire();
            CompleteResolution(expResponse, showExpiredNotice: true);
            return;
        }

        UpdateCountdownDisplay();
        UpdateSessionProperties();
    }

    private void UpdateCountdownDisplay()
    {
        if (_activeSession == null)
        {
            RemainingSecondsText = string.Empty;
            return;
        }

        int remainingSeconds = (int)Math.Max(0, Math.Ceiling(_activeSession.GetRemainingTime().TotalSeconds));
        RemainingSecondsText = $"{remainingSeconds} s";
    }

    private void UpdateSessionProperties()
    {
        OnPropertyChanged(nameof(CanAllow));
        OnPropertyChanged(nameof(CanDeny));
        OnPropertyChanged(nameof(AllowButtonLabel));
        OnPropertyChanged(nameof(Option2ButtonLabel));
        OnPropertyChanged(nameof(Option3ButtonLabel));
        OnPropertyChanged(nameof(Option4ButtonLabel));
        OnPropertyChanged(nameof(AllowButtonForeground));
        OnPropertyChanged(nameof(GracePeriodIndicator));
        OnPropertyChanged(nameof(ReviewRequiredBannerVisibility));
        OnPropertyChanged(nameof(ConfirmReviewButtonVisibility));
        OnPropertyChanged(nameof(AreAlwaysOptionsVisible));
        OnPropertyChanged(nameof(AlwaysOptionsVisibility));
        OnPropertyChanged(nameof(IsSafePrefixOptionVisible));
        OnPropertyChanged(nameof(SafePrefixOptionVisibility));
        OnPropertyChanged(nameof(SafePrefixOptionLabel));
        OnPropertyChanged(nameof(HotkeyGuideText));
        OnPropertyChanged(nameof(Option1BorderThickness));
        OnPropertyChanged(nameof(Option2BorderThickness));
        OnPropertyChanged(nameof(Option3BorderThickness));
        OnPropertyChanged(nameof(Option4BorderThickness));
        OnPropertyChanged(nameof(Option5BorderThickness));
    }

    /// <summary>
    /// Option 1: Allow once (no rule saved).
    /// </summary>
    private void ExecuteOption1()
    {
        if (_activeSession == null) return;
        if (_activeSession.TryAllow(out var response))
        {
            CompleteResolution(response);
        }
    }

    /// <summary>
    /// Option 2: Allow always in this conversation (RAM rule).
    /// </summary>
    private void ExecuteOption2()
    {
        if (_activeSession == null || !AreAlwaysOptionsVisible) return;
        if (_activeSession.TryAllow(out var response))
        {
            var rule = new ApprovalRule
            {
                Scope = ApprovalRuleScope.Conversation,
                ToolName = _activeSession.Request.ToolName,
                CommandPattern = _activeSession.Request.CommandLine ?? string.Empty,
                ConversationId = _activeSession.Request.ConversationId,
                WorkspaceFolder = _activeSession.Request.WorkspaceFolder
            };
            _ruleStore.AddRule(rule);

            CompleteResolution(response);
        }
    }

    /// <summary>
    /// Option 3: Allow always in this project (.antigravity/approval-rules.json).
    /// </summary>
    private void ExecuteOption3()
    {
        if (_activeSession == null || !AreAlwaysOptionsVisible) return;
        if (_activeSession.TryAllow(out var response))
        {
            string? workspaceRoot = _activeSession.Request.WorkspacePaths.Count > 0
                ? _activeSession.Request.WorkspacePaths[0]
                : _activeSession.Request.Cwd;

            var rule = new ApprovalRule
            {
                Scope = ApprovalRuleScope.Project,
                ToolName = _activeSession.Request.ToolName,
                CommandPattern = _activeSession.Request.CommandLine ?? string.Empty,
                WorkspaceKey = ApprovalRuleMatcher.NormalizePath(workspaceRoot ?? string.Empty),
                WorkspaceFolder = _activeSession.Request.WorkspaceFolder
            };
            _ruleStore.AddRule(rule, workspaceRoot);

            CompleteResolution(response);
        }
    }

    /// <summary>
    /// Option 3b: Allow always with safe prefix in this project.
    /// </summary>
    private void ExecuteOption3b()
    {
        if (_activeSession == null || !IsSafePrefixOptionVisible || string.IsNullOrWhiteSpace(_matchedSafePrefix)) return;
        if (_activeSession.TryAllow(out var response))
        {
            string? workspaceRoot = _activeSession.Request.WorkspacePaths.Count > 0
                ? _activeSession.Request.WorkspacePaths[0]
                : _activeSession.Request.Cwd;

            var rule = new ApprovalRule
            {
                Scope = ApprovalRuleScope.Project,
                ToolName = _activeSession.Request.ToolName,
                CommandPattern = _matchedSafePrefix,
                IsPrefixMatch = true,
                WorkspaceKey = ApprovalRuleMatcher.NormalizePath(workspaceRoot ?? string.Empty),
                WorkspaceFolder = _activeSession.Request.WorkspaceFolder
            };
            _ruleStore.AddRule(rule, workspaceRoot);

            CompleteResolution(response);
        }
    }

    /// <summary>
    /// Option 4: Allow always globally (%USERPROFILE%/.antigravity/approval-rules.json).
    /// </summary>
    private void ExecuteOption4()
    {
        if (_activeSession == null || !AreAlwaysOptionsVisible) return;
        if (_activeSession.TryAllow(out var response))
        {
            var rule = new ApprovalRule
            {
                Scope = ApprovalRuleScope.Global,
                ToolName = _activeSession.Request.ToolName,
                CommandPattern = _activeSession.Request.CommandLine ?? string.Empty
            };
            _ruleStore.AddRule(rule);

            CompleteResolution(response);
        }
    }

    /// <summary>
    /// Option 5: Deny with selected predefined reason.
    /// </summary>
    private void ExecuteOption5()
    {
        if (_activeSession == null) return;
        string reason = string.IsNullOrWhiteSpace(SelectedDenyReason) ? "Acción denegada por el usuario." : SelectedDenyReason;
        if (_activeSession.TryDeny(reason, out var response))
        {
            CompleteResolution(response);
        }
    }

    private void ExecuteDecideInAntigravity()
    {
        if (_activeSession == null) return;
        var response = _activeSession.DecideInAntigravity();
        AntigravityWindowActivator.TryActivateAntigravity();
        CompleteResolution(response);
    }

    private void ExecuteConfirmReview()
    {
        if (_activeSession == null) return;
        _isReviewConfirmed = true;
        _activeSession.MarkExpanded();
        UpdateSessionProperties();
    }

    private void CompleteResolution(ApprovalResponse response, bool showExpiredNotice = false)
    {
        _graceTimer?.Stop();
        _graceTimer = null;
        _countdownTimer?.Stop();
        _countdownTimer = null;

        if (_queue.Count > 0)
        {
            var finished = _queue[0];
            _queue.RemoveAt(0);
            finished.Tcs.TrySetResult(response);
        }

        UpdateQueueProperties();

        // Process next item in FIFO queue
        while (_queue.Count > 0)
        {
            var next = _queue[0];
            if (next.Session.HasTimedOut())
            {
                _queue.RemoveAt(0);
                next.Tcs.TrySetResult(next.Session.Expire());
                UpdateQueueProperties();
                continue;
            }

            ActivateQueueItem(next);
            return;
        }

        // All queued requests resolved
        UnregisterDynamicHotkeys();
        _activeSession = null;
        IsActive = false;

        WeakReferenceMessenger.Default.Send(new CollapseRequestedMessage());
    }

    public void OnHotkeyMessage(int hotkeyId)
    {
        if (_activeSession == null) return;

        switch (hotkeyId)
        {
            case HotkeyOption1Id:
                if (_activeSession.CanAllowViaHotkey())
                {
                    ExecuteOption1();
                }
                break;

            case HotkeyOption2Id:
                if (AreAlwaysOptionsVisible && _activeSession.CanAllowViaHotkey())
                {
                    ExecuteOption2();
                }
                break;

            case HotkeyOption3Id:
                if (AreAlwaysOptionsVisible && _activeSession.CanAllowViaHotkey())
                {
                    ExecuteOption3();
                }
                break;

            case HotkeyOption4Id:
                if (AreAlwaysOptionsVisible && _activeSession.CanAllowViaHotkey())
                {
                    ExecuteOption4();
                }
                break;

            case HotkeyOption5Id:
                if (_activeSession.CanDeny())
                {
                    ExecuteOption5();
                }
                break;

            case HotkeyEnterId:
                ExecuteHighlightedOption();
                break;

            case HotkeyDecideAgyId:
                ExecuteDecideInAntigravity();
                break;
        }
    }

    private void ExecuteHighlightedOption()
    {
        if (_activeSession == null) return;

        switch (_settings.AgentApprovalHighlightedOption)
        {
            case 1:
                if (_activeSession.CanAllowViaHotkey()) ExecuteOption1();
                break;

            case 2:
                if (AreAlwaysOptionsVisible && _activeSession.CanAllowViaHotkey()) ExecuteOption2();
                break;

            case 3:
                if (AreAlwaysOptionsVisible && _activeSession.CanAllowViaHotkey()) ExecuteOption3();
                break;

            case 4:
                if (AreAlwaysOptionsVisible && _activeSession.CanAllowViaHotkey()) ExecuteOption4();
                break;

            case 5:
                if (_activeSession.CanDeny()) ExecuteOption5();
                break;

            default:
                if (_activeSession.CanAllowViaHotkey()) ExecuteOption1();
                break;
        }
    }

    private void RegisterDynamicHotkeys()
    {
        if (_hwnd == IntPtr.Zero || _areHotkeysRegistered) return;

        uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;

        NativeMethods.RegisterHotKey(_hwnd, HotkeyOption1Id, mods, 0x31); // 1
        NativeMethods.RegisterHotKey(_hwnd, HotkeyOption2Id, mods, 0x32); // 2
        NativeMethods.RegisterHotKey(_hwnd, HotkeyOption3Id, mods, 0x33); // 3
        NativeMethods.RegisterHotKey(_hwnd, HotkeyOption4Id, mods, 0x34); // 4
        NativeMethods.RegisterHotKey(_hwnd, HotkeyOption5Id, mods, 0x35); // 5
        NativeMethods.RegisterHotKey(_hwnd, HotkeyEnterId, mods, 0x0D);   // Enter
        NativeMethods.RegisterHotKey(_hwnd, HotkeyDecideAgyId, mods, 0x41); // A

        _areHotkeysRegistered = true;
    }

    private void UnregisterDynamicHotkeys()
    {
        if (_hwnd == IntPtr.Zero || !_areHotkeysRegistered) return;

        NativeMethods.UnregisterHotKey(_hwnd, HotkeyOption1Id);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyOption2Id);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyOption3Id);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyOption4Id);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyOption5Id);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyEnterId);
        NativeMethods.UnregisterHotKey(_hwnd, HotkeyDecideAgyId);

        _areHotkeysRegistered = false;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _graceTimer?.Stop();
            _countdownTimer?.Stop();
            _autoAllowNoticeTimer?.Stop();
            UnregisterDynamicHotkeys();
        }
        base.Dispose(disposing);
    }

    private sealed record ApprovalQueueItem(ApprovalSessionPolicy Session, TaskCompletionSource<ApprovalResponse> Tcs);
}
