using System.Collections.ObjectModel;
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
/// Features auto-expansion without stealing terminal focus (WS_EX_NOACTIVATE),
/// 600 ms anti-accidental click guard, risk indicator badges, numbered options
/// matching Antigravity native dialog, and dynamic global hotkeys.
/// </summary>
public sealed class ApprovalWidget : IslandWidgetBase
{
    public const string WidgetId = "AgentApprovalWidget";
    public const int HotkeyOption1Id = 0xA001;
    public const int HotkeyOption5Id = 0xA005;
    public const int HotkeyEnterId = 0xA00E;
    public const int HotkeyDecideAgyId = 0xA00A;

    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private ApprovalSessionPolicy? _activeSession;
    private TaskCompletionSource<ApprovalResponse>? _tcs;
    private DispatcherTimer? _countdownTimer;
    private DispatcherTimer? _graceTimer;

    private bool _isReviewConfirmed;
    private IntPtr _hwnd = IntPtr.Zero;
    private bool _areHotkeysRegistered;

    private string _toolDisplayName = string.Empty;
    private string _projectFolder = "Workspace";
    private string _fullSummary = string.Empty;
    private string _collapsedSummary = string.Empty;
    private string _optionActionSummary = string.Empty;
    private string _remainingSecondsText = string.Empty;
    private string _selectedDenyReason = string.Empty;
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

    public string AllowButtonLabel
    {
        get
        {
            if (_activeSession?.IsInGracePeriod() == true)
            {
                return $"1. Sí, permitir esta vez ({OptionActionSummary}) - (Espera...)";
            }
            if (_activeSession?.Presentation.RequiresExpandedReview == true && !_isReviewConfirmed)
            {
                return $"1. Sí, permitir esta vez ({OptionActionSummary}) - (Revisión requerida)";
            }
            return $"1. Sí, permitir esta vez ({OptionActionSummary})";
        }
    }

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

    public string HotkeyGuideText => Risk == RiskLevel.High
        ? "Atajos: Ctrl+Alt+1 [Bloqueado por riesgo alto] • Ctrl+Alt+5 (Denegar) • Ctrl+Alt+A (Nativo)"
        : "Atajos: Ctrl+Alt+1 (Permitir) • Ctrl+Alt+5 (Denegar) • Ctrl+Alt+Enter (Resaltada) • Ctrl+Alt+A (Nativo)";

    public ICommand AllowCommand { get; }
    public ICommand DenyCommand { get; }
    public ICommand DecideInAntigravityCommand { get; }
    public ICommand ConfirmReviewCommand { get; }

    public ApprovalWidget(AppSettings settings, Dispatcher? dispatcher = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        Priority = ActivityPriority.AgentApproval;

        AllowCommand = new RelayCommand(ExecuteAllow);
        DenyCommand = new RelayCommand(ExecuteDeny);
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
    /// Handles an incoming approval session on the UI thread.
    /// Returns a task that resolves when user answers or session times out.
    /// </summary>
    public Task<ApprovalResponse> HandleRequestAsync(ApprovalSessionPolicy session)
    {
        return _dispatcher.InvokeAsync(() =>
        {
            _activeSession = session;
            _tcs = new TaskCompletionSource<ApprovalResponse>(TaskCreationOptions.RunContinuationsAsynchronously);
            _isReviewConfirmed = !session.Presentation.RequiresExpandedReview;

            var pres = session.Presentation;
            ToolDisplayName = pres.ToolDisplayName;
            ProjectFolder = pres.ProjectFolder;
            FullSummary = pres.FullSummary;
            CollapsedSummary = pres.CollapsedSummary;
            OptionActionSummary = pres.OptionActionSummary;
            Risk = pres.Risk;

            LoadPredefinedReasons();
            UpdateSessionProperties();

            // Sound indication if enabled
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

            // Start grace period timer (600 ms)
            _graceTimer?.Stop();
            _graceTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = session.GracePeriod
            };
            _graceTimer.Tick += (_, _) =>
            {
                _graceTimer.Stop();
                UpdateSessionProperties();
            };
            _graceTimer.Start();

            // Start countdown timer (1s interval)
            _countdownTimer?.Stop();
            _countdownTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _countdownTimer.Tick += OnCountdownTick;
            _countdownTimer.Start();
            UpdateCountdownDisplay();

            // Register dynamic hotkeys
            RegisterDynamicHotkeys();

            return _tcs.Task;
        }).Task.Unwrap();
    }

    public void HandleCancelled(string requestId)
    {
        _dispatcher.InvokeAsync(() =>
        {
            if (_activeSession != null && _activeSession.Request.Id == requestId)
            {
                CompleteResolution(_activeSession.CancelByClientDisconnect());
            }
        });
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
        OnPropertyChanged(nameof(AllowButtonForeground));
        OnPropertyChanged(nameof(GracePeriodIndicator));
        OnPropertyChanged(nameof(ReviewRequiredBannerVisibility));
        OnPropertyChanged(nameof(ConfirmReviewButtonVisibility));
    }

    private void ExecuteAllow()
    {
        if (_activeSession == null) return;
        if (_activeSession.TryAllow(out var response))
        {
            CompleteResolution(response);
        }
    }

    private void ExecuteDeny()
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

        UnregisterDynamicHotkeys();

        _tcs?.TrySetResult(response);
        _activeSession = null;
        IsActive = false;

        if (showExpiredNotice)
        {
            // Transient notice in the notch per task 11
            WeakReferenceMessenger.Default.Send(new CollapseRequestedMessage());
        }
        else
        {
            WeakReferenceMessenger.Default.Send(new CollapseRequestedMessage());
        }
    }

    public void OnHotkeyMessage(int hotkeyId)
    {
        if (_activeSession == null) return;

        switch (hotkeyId)
        {
            case HotkeyOption1Id:
                if (_activeSession.CanAllowViaHotkey())
                {
                    ExecuteAllow();
                }
                break;

            case HotkeyOption5Id:
                if (_activeSession.CanDeny())
                {
                    ExecuteDeny();
                }
                break;

            case HotkeyEnterId:
                if (_settings.AgentApprovalHighlightedOption == 1 && _activeSession.CanAllowViaHotkey())
                {
                    ExecuteAllow();
                }
                else if (_settings.AgentApprovalHighlightedOption == 5 && _activeSession.CanDeny())
                {
                    ExecuteDeny();
                }
                break;

            case HotkeyDecideAgyId:
                ExecuteDecideInAntigravity();
                break;
        }
    }

    private void RegisterDynamicHotkeys()
    {
        if (_hwnd == IntPtr.Zero || _areHotkeysRegistered) return;

        // Ctrl + Alt
        uint mods = NativeMethods.MOD_CONTROL | NativeMethods.MOD_ALT | NativeMethods.MOD_NOREPEAT;

        // 1 = 0x31
        NativeMethods.RegisterHotKey(_hwnd, HotkeyOption1Id, mods, 0x31);
        // 5 = 0x35
        NativeMethods.RegisterHotKey(_hwnd, HotkeyOption5Id, mods, 0x35);
        // Enter = 0x0D
        NativeMethods.RegisterHotKey(_hwnd, HotkeyEnterId, mods, 0x0D);
        // A = 0x41
        NativeMethods.RegisterHotKey(_hwnd, HotkeyDecideAgyId, mods, 0x41);

        _areHotkeysRegistered = true;
    }

    private void UnregisterDynamicHotkeys()
    {
        if (_hwnd == IntPtr.Zero || !_areHotkeysRegistered) return;

        NativeMethods.UnregisterHotKey(_hwnd, HotkeyOption1Id);
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
            UnregisterDynamicHotkeys();
        }
        base.Dispose(disposing);
    }
}
