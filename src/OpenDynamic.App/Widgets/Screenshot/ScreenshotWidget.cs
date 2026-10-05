using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Native;
using OpenDynamic.App.Services;
using OpenDynamic.App.Widgets.Messages;
using OpenDynamic.App.Widgets.Screenshot.Views;
using OpenDynamic.Core.Screenshots;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.Screenshot;

/// <summary>
/// Transient and expandable Dynamic Island widget for newly saved screenshots (Priority 75, 6s default).
/// Adheres strictly to:
/// - Non-locking in-memory thumbnail display (BitmapCacheOption.OnLoad + Freeze), released immediately on close.
/// - Drag &amp; Drop strictly with DragDropEffects.Copy (never Move).
/// - Recycle Bin deletion with explicit double confirmation and Win32 SHFileOperationW (FOF_ALLOWUNDO).
/// - Zero PII in logs (only extension, size, or dimensions).
/// </summary>
public sealed class ScreenshotWidget : IslandWidgetBase
{
    public const string WidgetId = "screenshot";

    private readonly ScreenshotWatcherService _watcherService;
    private readonly ClipboardService _clipboardService;
    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;

    private DispatcherTimer? _transientTimer;
    private DispatcherTimer? _feedbackTimer;

    private ScreenshotEntry? _currentEntry;
    private BitmapSource? _currentThumbnail;
    private string _transientTitle = "Captura guardada";
    private string _transientSubtitle = string.Empty;
    private string? _feedbackMessage;
    private bool _isShowingHistory;
    private int _trashConfirmationStep;
    private bool _isPointerHovering;

    public override string Id => WidgetId;

    public ScreenshotEntry? CurrentEntry
    {
        get => _currentEntry;
        private set
        {
            if (SetProperty(ref _currentEntry, value))
            {
                OnPropertyChanged(nameof(HasCurrentEntry));
                OnPropertyChanged(nameof(IsCurrentEntryAvailable));
                OnPropertyChanged(nameof(CurrentFileName));
                OnPropertyChanged(nameof(CurrentMetadata));
                OnPropertyChanged(nameof(CurrentExtensionBadge));
            }
        }
    }

    public BitmapSource? CurrentThumbnail
    {
        get => _currentThumbnail;
        private set
        {
            if (SetProperty(ref _currentThumbnail, value))
            {
                OnPropertyChanged(nameof(HasThumbnail));
            }
        }
    }

    public bool HasCurrentEntry => _currentEntry != null;
    public bool IsCurrentEntryAvailable => _currentEntry?.IsAvailable == true;
    public bool HasThumbnail => _settings.ShowScreenshotThumbnail && _currentThumbnail != null;
    public bool ShowThumbnailSetting => _settings.ShowScreenshotThumbnail;
    public bool IsTrashActionEnabled => _settings.EnableScreenshotTrashAction;

    public string CurrentFileName => _currentEntry?.FileName ?? "Sin captura seleccionada";
    public string CurrentMetadata => _currentEntry?.MetadataSummary ?? string.Empty;
    public string CurrentExtensionBadge =>
        !string.IsNullOrWhiteSpace(_currentEntry?.Extension)
            ? _currentEntry.Extension.TrimStart('.').ToUpperInvariant()
            : "IMG";

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
        private set
        {
            if (SetProperty(ref _feedbackMessage, value))
            {
                OnPropertyChanged(nameof(HasFeedback));
            }
        }
    }

    public bool HasFeedback => !string.IsNullOrEmpty(_feedbackMessage);

    public bool IsShowingHistory
    {
        get => _isShowingHistory;
        set
        {
            if (SetProperty(ref _isShowingHistory, value))
            {
                OnPropertyChanged(nameof(IsShowingPreview));
                OnPropertyChanged(nameof(ModeToggleLabel));
            }
        }
    }

    public bool IsShowingPreview => !_isShowingHistory;
    public string ModeToggleLabel => _isShowingHistory ? "Vista previa" : $"Recientes ({RecentItems.Count})";

    /// <summary>
    /// 0 = normal state, 1 = first confirmation requested, 2 = second (final) confirmation requested.
    /// </summary>
    public int TrashConfirmationStep
    {
        get => _trashConfirmationStep;
        private set
        {
            if (SetProperty(ref _trashConfirmationStep, value))
            {
                OnPropertyChanged(nameof(IsTrashStep0));
                OnPropertyChanged(nameof(IsTrashStep1));
                OnPropertyChanged(nameof(IsTrashStep2));
            }
        }
    }

    public bool IsTrashStep0 => _trashConfirmationStep == 0;
    public bool IsTrashStep1 => _trashConfirmationStep == 1;
    public bool IsTrashStep2 => _trashConfirmationStep == 2;

    public ObservableCollection<ScreenshotEntry> RecentItems { get; } = new();

    public ScreenshotWidget(
        ScreenshotWatcherService watcherService,
        ClipboardService clipboardService,
        AppSettings settings,
        Dispatcher? dispatcher = null)
        : base(settings?.DefaultScreenshotPriority ?? ActivityPriority.Screenshot)
    {
        _watcherService = watcherService ?? throw new ArgumentNullException(nameof(watcherService));
        _clipboardService = clipboardService ?? throw new ArgumentNullException(nameof(clipboardService));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? (Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher);
    }

    public override void Initialize()
    {
        base.Initialize();

        try
        {
            _watcherService.ScreenshotCaptured += OnScreenshotCaptured;
            _watcherService.HistoryChanged += OnHistoryChanged;
            Log.Debug("ScreenshotWidget initialized successfully.");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize ScreenshotWidget.");
        }
    }

    private void OnScreenshotCaptured(object? sender, ScreenshotCapturedEventArgs e)
    {
        if (!_settings.EnableScreenshotWidget)
        {
            return;
        }

        _dispatcher.InvokeAsync(() =>
        {
            TrashConfirmationStep = 0;
            FeedbackMessage = null;
            IsShowingHistory = false;

            CurrentEntry = e.Entry;
            CurrentThumbnail = _settings.ShowScreenshotThumbnail ? e.Thumbnail : null;
            TransientTitle = "Captura guardada";
            TransientSubtitle = e.Entry.MetadataSummary;

            OnPropertyChanged(nameof(ShowThumbnailSetting));
            OnPropertyChanged(nameof(IsTrashActionEnabled));

            RefreshRecentItems();

            if (DisplayMode == WidgetDisplayMode.Expanded)
            {
                return;
            }

            var duration = GetConfiguredTransientDuration();
            Activate(transientDuration: duration, priorityOverride: _settings.DefaultScreenshotPriority);

            if (!_isPointerHovering)
            {
                ResetTransientTimer(duration);
            }
            else
            {
                PauseTransientExpirationForHover();
            }
        });
    }

    private void OnHistoryChanged(object? sender, EventArgs e)
    {
        _dispatcher.InvokeAsync(RefreshRecentItems);
    }

    public void RefreshRecentItems()
    {
        var items = _watcherService.GetRefreshedHistory();
        RecentItems.Clear();
        foreach (var item in items)
        {
            RecentItems.Add(item);
        }

        if (_currentEntry != null)
        {
            var updatedCurrent = items.FirstOrDefault(i =>
                string.Equals(i.FilePath, _currentEntry.FilePath, StringComparison.OrdinalIgnoreCase));
            if (updatedCurrent != null)
            {
                CurrentEntry = updatedCurrent;
            }
            else if (!File.Exists(_currentEntry.FilePath))
            {
                CurrentEntry = _currentEntry with { IsAvailable = false };
                CurrentThumbnail = null;
            }
        }

        OnPropertyChanged(nameof(ModeToggleLabel));
    }

    /// <summary>
    /// Pauses transient auto-expiration while the mouse pointer hovers over the widget.
    /// </summary>
    public void OnViewMouseEnter()
    {
        _isPointerHovering = true;
        if (DisplayMode != WidgetDisplayMode.Expanded && IsActive)
        {
            PauseTransientExpirationForHover();
        }
    }

    /// <summary>
    /// Resumes transient auto-expiration when the mouse pointer leaves the compact widget.
    /// </summary>
    public void OnViewMouseLeave()
    {
        _isPointerHovering = false;
        if (DisplayMode != WidgetDisplayMode.Expanded && IsActive)
        {
            var duration = GetConfiguredTransientDuration();
            IsTransient = true;
            TransientDuration = duration;
            ResetTransientTimer(duration);
        }
    }

    public void ToggleHistoryView()
    {
        TrashConfirmationStep = 0;
        RefreshRecentItems();
        IsShowingHistory = !IsShowingHistory;
    }

    public void SelectHistoryItem(ScreenshotEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        TrashConfirmationStep = 0;
        RefreshRecentItems();

        var refreshed = RecentItems.FirstOrDefault(i =>
            string.Equals(i.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase)) ?? entry;

        if (!refreshed.IsAvailable || !ValidateFileOnDisk(refreshed.FilePath))
        {
            CurrentEntry = refreshed with { IsAvailable = false };
            CurrentThumbnail = null;
            ShowFeedback("Archivo no disponible", collapseOnFinish: false);
            return;
        }

        CurrentEntry = refreshed;
        if (_settings.ShowScreenshotThumbnail &&
            ScreenshotWatcherService.TryLoadFrozenBitmapFromDisk(
                refreshed.FilePath,
                ScreenshotWatcherService.ThumbnailDecodePixelWidth,
                out BitmapSource? bmp,
                out _,
                out _))
        {
            CurrentThumbnail = bmp;
        }
        else
        {
            CurrentThumbnail = null;
        }

        IsShowingHistory = false;
    }

    public void RemoveHistoryItem(ScreenshotEntry? entry)
    {
        if (entry == null)
        {
            return;
        }

        _watcherService.RemoveFromHistory(entry.FilePath);
        RefreshRecentItems();

        if (_currentEntry != null &&
            string.Equals(_currentEntry.FilePath, entry.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            var nextAvailable = RecentItems.FirstOrDefault(i => i.IsAvailable);
            if (nextAvailable != null)
            {
                SelectHistoryItem(nextAvailable);
            }
            else
            {
                CurrentEntry = null;
                CurrentThumbnail = null;
                IsShowingHistory = true;
            }
        }
    }

    public async Task CopyCurrentImageAsync()
    {
        var entry = _currentEntry;
        if (entry == null || !ValidateFileOnDisk(entry.FilePath))
        {
            RefreshRecentItems();
            ShowFeedback("Archivo no disponible", collapseOnFinish: false);
            return;
        }

        if (!ScreenshotWatcherService.TryLoadFrozenBitmapFromDisk(
            entry.FilePath,
            decodePixelWidth: null,
            out BitmapSource? fullBitmap,
            out _,
            out _) || fullBitmap == null)
        {
            ShowFeedback("Error al leer imagen", collapseOnFinish: false);
            return;
        }

        bool copied = await _clipboardService.CopyImageToClipboardAsync(fullBitmap);
        if (copied)
        {
            ShowFeedback("Imagen copiada", collapseOnFinish: false);
        }
        else
        {
            ShowFeedback("Portapapeles ocupado", collapseOnFinish: false);
        }
    }

    public void OpenCurrentImage()
    {
        var entry = _currentEntry;
        if (entry == null || !ValidateFileOnDisk(entry.FilePath))
        {
            RefreshRecentItems();
            ShowFeedback("Archivo no disponible", collapseOnFinish: false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = entry.FilePath,
                UseShellExecute = true
            });
            Log.Information("Opened screenshot in default viewer (Ext={Extension}).", entry.Extension);
            ShowFeedback("Abriendo captura", collapseOnFinish: true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to open screenshot in default viewer.");
            ShowFeedback("Error al abrir", collapseOnFinish: false);
        }
    }

    public void RevealCurrentInFolder()
    {
        var entry = _currentEntry;
        if (entry == null || !ValidateFileOnDisk(entry.FilePath))
        {
            RefreshRecentItems();
            ShowFeedback("Archivo no disponible", collapseOnFinish: false);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"/select,\"{entry.FilePath}\"",
                UseShellExecute = true
            });
            Log.Information("Revealed screenshot in File Explorer (Ext={Extension}).", entry.Extension);
            ShowFeedback("Mostrando en carpeta", collapseOnFinish: true);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to reveal screenshot in File Explorer.");
            ShowFeedback("Error al abrir carpeta", collapseOnFinish: false);
        }
    }

    /// <summary>
    /// Initiates a native Drag &amp; Drop operation strictly with <see cref="DragDropEffects.Copy"/> (never Move).
    /// </summary>
    public bool TryStartFileDrag(DependencyObject dragSource)
    {
        if (dragSource == null)
        {
            return false;
        }

        var entry = _currentEntry;
        if (entry == null || !ValidateFileOnDisk(entry.FilePath))
        {
            RefreshRecentItems();
            ShowFeedback("Archivo no disponible", collapseOnFinish: false);
            return false;
        }

        try
        {
            var dataObject = new DataObject(DataFormats.FileDrop, new[] { entry.FilePath });
            DragDrop.DoDragDrop(dragSource, dataObject, DragDropEffects.Copy);
            Log.Information("Completed FileDrop drag operation with DragDropEffects.Copy (Ext={Extension}).", entry.Extension);
            return true;
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Error during screenshot Drag & Drop operation.");
            return false;
        }
    }

    public void RequestTrashStep1()
    {
        if (!_settings.EnableScreenshotTrashAction || _currentEntry == null)
        {
            return;
        }

        if (!ValidateFileOnDisk(_currentEntry.FilePath))
        {
            RefreshRecentItems();
            ShowFeedback("Archivo no disponible", collapseOnFinish: false);
            return;
        }

        TrashConfirmationStep = 1;
    }

    public void AdvanceTrashStep2()
    {
        if (_trashConfirmationStep == 1 && _currentEntry != null)
        {
            TrashConfirmationStep = 2;
        }
    }

    public void CancelTrashConfirmation()
    {
        TrashConfirmationStep = 0;
    }

    public void ConfirmSendToRecycleBin()
    {
        if (_trashConfirmationStep < 2 || !_settings.EnableScreenshotTrashAction)
        {
            return;
        }

        TrashConfirmationStep = 0;
        var entry = _currentEntry;
        if (entry == null || !ValidateFileOnDisk(entry.FilePath))
        {
            RefreshRecentItems();
            ShowFeedback("Archivo no disponible", collapseOnFinish: false);
            return;
        }

        // Release our in-memory thumbnail reference before recycling
        CurrentThumbnail = null;

        bool recycled = NativeMethods.SendFileToRecycleBin(entry.FilePath);
        if (recycled)
        {
            Log.Information("Screenshot sent to Recycle Bin via SHFileOperationW (Ext={Extension}, SizeBytes={SizeBytes}).",
                entry.Extension, entry.FileSizeBytes);

            _watcherService.RemoveFromHistory(entry.FilePath);
            RefreshRecentItems();

            var nextAvailable = RecentItems.FirstOrDefault(i => i.IsAvailable);
            if (nextAvailable != null)
            {
                SelectHistoryItem(nextAvailable);
                ShowFeedback("Enviada a la papelera", collapseOnFinish: false);
            }
            else
            {
                CurrentEntry = null;
                ShowFeedback("Enviada a la papelera", collapseOnFinish: true);
            }
        }
        else
        {
            ShowFeedback("No se pudo reciclar", collapseOnFinish: false);
        }
    }

    public override void SetDisplayState(WidgetDisplayMode mode, bool isVisible)
    {
        base.SetDisplayState(mode, isVisible);

        if (mode == WidgetDisplayMode.Expanded)
        {
            _transientTimer?.Stop();
            _transientTimer = null;
            IsTransient = false;
            TransientDuration = null;
        }
    }

    public override void OnExpand()
    {
        base.OnExpand();

        _transientTimer?.Stop();
        _transientTimer = null;
        IsTransient = false;
        TransientDuration = null;
        TrashConfirmationStep = 0;

        OnPropertyChanged(nameof(ShowThumbnailSetting));
        OnPropertyChanged(nameof(IsTrashActionEnabled));

        RefreshRecentItems();

        if (_currentEntry == null || !_currentEntry.IsAvailable)
        {
            var latestAvailable = RecentItems.FirstOrDefault(i => i.IsAvailable);
            if (latestAvailable != null)
            {
                SelectHistoryItem(latestAvailable);
            }
            else
            {
                IsShowingHistory = true;
            }
        }
        else if (_settings.ShowScreenshotThumbnail && _currentThumbnail == null)
        {
            if (ScreenshotWatcherService.TryLoadFrozenBitmapFromDisk(
                _currentEntry.FilePath,
                ScreenshotWatcherService.ThumbnailDecodePixelWidth,
                out BitmapSource? bmp,
                out _,
                out _))
            {
                CurrentThumbnail = bmp;
            }
        }
    }

    public override void OnCollapse()
    {
        base.OnCollapse();

        TrashConfirmationStep = 0;
        FeedbackMessage = null;
        _feedbackTimer?.Stop();
        _feedbackTimer = null;
        _isPointerHovering = false;

        // Release in-memory thumbnail immediately when closing the notice
        CurrentThumbnail = null;

        Deactivate();
    }

    private bool ValidateFileOnDisk(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        var watchedFolders = _watcherService.GetWatchedFoldersSnapshot();
        return ScreenshotFileFilter.ValidateSafeImageFileOnDisk(filePath, watchedFolders);
    }

    private TimeSpan GetConfiguredTransientDuration()
    {
        double seconds = _settings.ScreenshotTransientDurationSeconds > 0
            ? _settings.ScreenshotTransientDurationSeconds
            : 6.0;
        return TimeSpan.FromSeconds(Math.Clamp(seconds, 2.0, 15.0));
    }

    private void PauseTransientExpirationForHover()
    {
        _transientTimer?.Stop();
        _transientTimer = null;
        IsTransient = false;
        TransientDuration = null;
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

            if (DisplayMode == WidgetDisplayMode.Expanded || _isPointerHovering || !IsTransient)
            {
                return;
            }

            CurrentThumbnail = null;
            Deactivate();
        };
        _transientTimer.Start();
    }

    private void ShowFeedback(string message, bool collapseOnFinish)
    {
        FeedbackMessage = message;
        _feedbackTimer?.Stop();
        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1100) };
        _feedbackTimer.Tick += (s, e) =>
        {
            _feedbackTimer.Stop();
            _feedbackTimer = null;
            FeedbackMessage = null;

            if (collapseOnFinish && DisplayMode == WidgetDisplayMode.Expanded)
            {
                WeakReferenceMessenger.Default.Send(new CollapseRequestedMessage());
            }
        };
        _feedbackTimer.Start();
    }

    public override UserControl? CreateCompactView() => new ScreenshotCompactView { DataContext = this };

    public override UserControl? CreateExpandedView() => new ScreenshotExpandedView { DataContext = this };

    public override UserControl? CreateSplitView() => new ScreenshotSplitView { DataContext = this };

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _transientTimer?.Stop();
            _transientTimer = null;

            _feedbackTimer?.Stop();
            _feedbackTimer = null;

            CurrentThumbnail = null;

            _watcherService.ScreenshotCaptured -= OnScreenshotCaptured;
            _watcherService.HistoryChanged -= OnHistoryChanged;
        }

        base.Dispose(disposing);
    }
}
