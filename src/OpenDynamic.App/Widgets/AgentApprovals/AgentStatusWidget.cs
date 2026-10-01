using System.Media;
using System.Windows.Controls;
using System.Windows.Threading;
using OpenDynamic.App.Widgets.AgentApprovals.Views;
using OpenDynamic.Core.AgentApprovals;
using OpenDynamic.Core.Settings;
using OpenDynamic.Core.Widgets;
using Serilog;

namespace OpenDynamic.App.Widgets.AgentApprovals;

/// <summary>
/// Transient notch notification widget for Antigravity task completion (Stop hook) (Priority 70, Task 8).
/// Displays a subtle notice ("Antigravity terminó en &lt;proyecto&gt;") for 4 seconds strictly when fullyIdle is true.
/// </summary>
public sealed class AgentStatusWidget : IslandWidgetBase
{
    public const string WidgetId = "AgentStatusWidget";
    public const int DefaultDurationSeconds = 4;

    private readonly AppSettings _settings;
    private readonly Dispatcher _dispatcher;
    private DispatcherTimer? _dismissTimer;
    private string _statusMessage = string.Empty;

    public override string Id => WidgetId;

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public AgentStatusWidget(AppSettings settings, Dispatcher? dispatcher = null)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _dispatcher = dispatcher ?? System.Windows.Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher;

        Priority = ActivityPriority.AgentStatus;
    }

    public override void Initialize()
    {
        // Ready for status events
    }

    public override UserControl? CreateCompactView() => new AgentStatusCompactView { DataContext = this };
    public override UserControl? CreateExpandedView() => CreateCompactView();
    public override UserControl? CreateSplitView() => CreateCompactView();

    /// <summary>
    /// Processes an agent lifecycle status event. Only notifies if fullyIdle is true.
    /// </summary>
    public void HandleStatusEvent(AgentStatusEvent statusEvent)
    {
        ArgumentNullException.ThrowIfNull(statusEvent);

        if (!_settings.EnableAgentStatusNotifications)
        {
            return;
        }

        // Golden Rule: Only notify when the agent is fully idle (all background tasks finished)
        if (!statusEvent.FullyIdle)
        {
            Log.Debug("Agent status ignored because fullyIdle is false. ConvId={ConvId}", statusEvent.ConversationId);
            return;
        }

        _dispatcher.InvokeAsync(() =>
        {
            string folder = string.IsNullOrWhiteSpace(statusEvent.WorkspaceFolder) ? "Workspace" : statusEvent.WorkspaceFolder;
            StatusMessage = $"Antigravity terminó en {folder}";

            IsActive = true;

            if (_settings.EnableAgentStatusSound)
            {
                try
                {
                    SystemSounds.Asterisk.Play();
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "Failed to play notification sound for agent completion.");
                }
            }

            _dismissTimer?.Stop();
            _dismissTimer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
            {
                Interval = TimeSpan.FromSeconds(DefaultDurationSeconds)
            };
            _dismissTimer.Tick += (_, _) =>
            {
                _dismissTimer.Stop();
                _dismissTimer = null;
                IsActive = false;
            };
            _dismissTimer.Start();
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _dismissTimer?.Stop();
        }
        base.Dispose(disposing);
    }
}
