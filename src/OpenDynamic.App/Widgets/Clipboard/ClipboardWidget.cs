using System.Collections.ObjectModel;
using System.Windows.Controls;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Clipboard.Views;
using OpenDynamic.App.Widgets.Messages;
using OpenDynamic.Core.Clipboard;
using OpenDynamic.Core.Settings;
using Serilog;

namespace OpenDynamic.App.Widgets.Clipboard;

/// <summary>
/// Transient and expanded widget for recent clipboard history.
/// Operates strictly with in-memory volatile retention (Golden Rule 10).
/// Registered with Priority 55, auto-expires after 2 seconds in transient mode.
/// </summary>
public sealed class ClipboardWidget : IslandWidgetBase
{
    public const string WidgetId = "clipboard";

    private readonly ClipboardService _clipboardService;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private DispatcherTimer? _transientTimer;
    private DispatcherTimer? _feedbackTimer;

    // Observable UI properties
    private ClipboardItemKind _currentKind = ClipboardItemKind.Text;
    private string _transientTitle = "Copiado";
    private string _transientSubtitle = string.Empty;
    private string? _feedbackMessage;

    public override string Id => WidgetId;

    public ClipboardItemKind CurrentKind
    {
        get => _currentKind;
        private set
        {
            if (SetProperty(ref _currentKind, value))
            {
                OnPropertyChanged(nameof(IsText));
                OnPropertyChanged(nameof(IsUrl));
                OnPropertyChanged(nameof(IsImage));
                OnPropertyChanged(nameof(IsFiles));
            }
        }
    }

    public string TransientTitle
    {
        get => _transientTitle;
        private set => SetProperty(ref _transientTitle, value);
    }

    public string TransientSubtitle
    {
        get => _transientSubtitle;
        private set => SetProperty(ref _transientSubtitle, value);
    }

    public string? FeedbackMessage
    {
        get => _feedbackMessage;
        set
        {
            if (SetProperty(ref _feedbackMessage, value))
            {
                OnPropertyChanged(nameof(HasFeedback));
            }
        }
    }

    public bool HasFeedback => !string.IsNullOrEmpty(_feedbackMessage);

    public bool IsText => CurrentKind == ClipboardItemKind.Text;
    public bool IsUrl => CurrentKind == ClipboardItemKind.Url;
    public bool IsImage => CurrentKind == ClipboardItemKind.Image;
    public bool IsFiles => CurrentKind == ClipboardItemKind.Files;

    public ObservableCollection<ClipboardItem> RecentItems { get; } = new();

    public ClipboardWidget(
        ClipboardService clipboardService,
        AppSettings settings,
        Dispatcher? dispatcher = null)
        : base(settings?.DefaultClipboardPriority ?? 55)
    {
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? (System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    public override void Initialize()
    {
        base.Initialize();

        try
        {
            _clipboardService.ClipboardItemCaptured += OnClipboardItemCaptured;
            _clipboardService.HistoryCleared += OnHistoryCleared;
            Log.Debug("ClipboardWidget initialized successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize ClipboardWidget.");
        }
    }

    private void OnClipboardItemCaptured(object? sender, ClipboardItem item)
    {
        if (!_settings.EnableClipboardWidget) return;

        _dispatcher.InvokeAsync(() =>
        {
            CurrentKind = item.Kind;

            // Resolve transient title & subtitle
            string title;
            string subtitle = string.Empty;

            switch (item.Kind)
            {
                case ClipboardItemKind.Url:
                    title = "Enlace copiado";
                    subtitle = _settings.ShowClipboardPreview ? item.DisplayPreview : string.Empty;
                    break;

                case ClipboardItemKind.Image:
                    title = "Imagen copiada";
                    subtitle = string.Empty;
                    break;

                case ClipboardItemKind.Files:
                    title = "Archivos copiados";
                    subtitle = item.DisplayPreview; // e.g. "3 archivos"
                    break;

                case ClipboardItemKind.Text:
                default:
                    title = "Texto copiado";
                    subtitle = _settings.ShowClipboardPreview ? item.DisplayPreview : string.Empty;
                    break;
            }

            TransientTitle = title;
            TransientSubtitle = subtitle;

            RefreshRecentItems();

            // If the widget is already expanded, do not start transient auto-close timer
            if (DisplayMode == OpenDynamic.Core.Widgets.WidgetDisplayMode.Expanded || !IsTransient)
            {
                return;
            }

            var duration = TimeSpan.FromSeconds(_settings.ClipboardTransientDurationSeconds > 0
                ? _settings.ClipboardTransientDurationSeconds
                : 2.0);

            // Activate transient notice with Priority 55
            Activate(transientDuration: duration, priorityOverride: _settings.DefaultClipboardPriority);
            ResetTransientTimer(duration);
        });
    }

    private void OnHistoryCleared(object? sender, EventArgs e)
    {
        _dispatcher.InvokeAsync(() =>
        {
            RecentItems.Clear();
            FeedbackMessage = null;
            if (IsActive && DisplayMode != OpenDynamic.Core.Widgets.WidgetDisplayMode.Expanded)
            {
                Deactivate();
            }
        });
    }

    public void RefreshRecentItems()
    {
        var items = _clipboardService.HistoryManager.GetRecentItems();
        RecentItems.Clear();
        foreach (var it in items)
        {
            RecentItems.Add(it);
        }
    }

    public void ReCopyItem(ClipboardItem? item)
    {
        if (item == null || string.IsNullOrEmpty(item.RawContent)) return;

        _clipboardService.CopyToClipboard(item.RawContent);

        // Show non-focus-stealing feedback
        FeedbackMessage = "Copiado de nuevo";
        _feedbackTimer?.Stop();
        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _feedbackTimer.Tick += (s, e) =>
        {
            _feedbackTimer.Stop();
            _feedbackTimer = null;
            FeedbackMessage = null;

            // Gracefully collapse after confirming re-copy to the user
            if (DisplayMode == OpenDynamic.Core.Widgets.WidgetDisplayMode.Expanded)
            {
                WeakReferenceMessenger.Default.Send(new CollapseRequestedMessage());
            }
        };
        _feedbackTimer.Start();
    }

    public void ClearAll()
    {
        _clipboardService.ClearHistory();
        FeedbackMessage = "Historial borrado";
        _feedbackTimer?.Stop();
        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(900) };
        _feedbackTimer.Tick += (s, e) =>
        {
            _feedbackTimer.Stop();
            _feedbackTimer = null;
            FeedbackMessage = null;

            if (DisplayMode == OpenDynamic.Core.Widgets.WidgetDisplayMode.Expanded)
            {
                WeakReferenceMessenger.Default.Send(new CollapseRequestedMessage());
            }
        };
        _feedbackTimer.Start();
    }

    public override void SetDisplayState(OpenDynamic.Core.Widgets.WidgetDisplayMode mode, bool isVisible)
    {
        base.SetDisplayState(mode, isVisible);

        if (mode == OpenDynamic.Core.Widgets.WidgetDisplayMode.Expanded)
        {
            // Immediately stop any transient timer and pause transient status while expanded
            _transientTimer?.Stop();
            _transientTimer = null;
            IsTransient = false;
            TransientDuration = null;
        }
    }

    public override void OnExpand()
    {
        base.OnExpand();

        // 1. Immediately cancel the transient auto-close timer so the expanded view remains open indefinitely
        _transientTimer?.Stop();
        _transientTimer = null;

        // 2. Pause transient status while expanded so PriorityResolver does not discard the widget
        IsTransient = false;
        TransientDuration = null;

        // 3. Refresh items in memory
        RefreshRecentItems();
    }

    public override void OnCollapse()
    {
        base.OnCollapse();
        FeedbackMessage = null;
        _feedbackTimer?.Stop();
        _feedbackTimer = null;

        // Once collapsed from expanded view, complete the clipboard interaction cycle cleanly
        Deactivate();
    }

    private void ResetTransientTimer(TimeSpan duration)
    {
        _transientTimer?.Stop();
        _transientTimer = new DispatcherTimer
        {
            Interval = duration
        };
        _transientTimer.Tick += (s, e) =>
        {
            _transientTimer.Stop();
            _transientTimer = null;

            // Defensive guard: never deactivate if the user is currently in Expanded mode or non-transient
            if (DisplayMode == OpenDynamic.Core.Widgets.WidgetDisplayMode.Expanded || !IsTransient)
            {
                Log.Debug("ClipboardWidget transient lifespan expired while in Expanded mode or non-transient; ignoring deactivation.");
                return;
            }

            Log.Debug("ClipboardWidget transient lifespan expired. Deactivating.");
            Deactivate();
        };
        _transientTimer.Start();
    }

    public override UserControl? CreateCompactView() => new ClipboardCompactView { DataContext = this };

    public override UserControl? CreateExpandedView() => new ClipboardExpandedView { DataContext = this };

    public override UserControl? CreateSplitView() => new ClipboardSplitView { DataContext = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _transientTimer?.Stop();
            _transientTimer = null;

            _feedbackTimer?.Stop();
            _feedbackTimer = null;

            _clipboardService.ClipboardItemCaptured -= OnClipboardItemCaptured;
            _clipboardService.HistoryCleared -= OnHistoryCleared;
        }

        base.Dispose(disposing);
    }
}
