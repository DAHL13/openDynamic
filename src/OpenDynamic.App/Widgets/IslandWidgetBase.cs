using System.Windows.Controls;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Widgets.Messages;
using OpenDynamic.Core.Widgets;

namespace OpenDynamic.App.Widgets;

/// <summary>
/// Abstract base class implementing <see cref="IIslandWidget"/> with CommunityToolkit.Mvvm support,
/// automatic weak messaging dispatch, and standard lifecycle hooks.
/// </summary>
public abstract class IslandWidgetBase : ObservableObject, IIslandWidget
{
    private int _priority;
    private bool _isActive;
    private bool _isTransient;
    private DateTimeOffset? _lastActivatedUtc;
    private TimeSpan? _transientDuration;
    private IslandActivity? _currentActivity;
    private WidgetDisplayMode _displayMode = WidgetDisplayMode.Compact;
    private bool _isVisibleOnIsland;
    private bool _isDisposed;

    public abstract string Id { get; }

    public int Priority
    {
        get => _priority;
        set
        {
            if (SetProperty(ref _priority, value))
            {
                NotifyChanged();
            }
        }
    }

    public bool IsActive
    {
        get => _isActive;
        protected set
        {
            if (SetProperty(ref _isActive, value))
            {
                if (value)
                {
                    LastActivatedUtc = DateTimeOffset.UtcNow;
                }
                NotifyChanged();
            }
        }
    }

    public bool IsTransient
    {
        get => _isTransient;
        protected set
        {
            if (SetProperty(ref _isTransient, value))
            {
                NotifyChanged();
            }
        }
    }

    public virtual ActivityActivationMode ActivationMode { get; protected set; } = ActivityActivationMode.Event;

    public DateTimeOffset? LastActivatedUtc
    {
        get => _lastActivatedUtc;
        protected set => SetProperty(ref _lastActivatedUtc, value);
    }

    public TimeSpan? TransientDuration
    {
        get => _transientDuration;
        protected set => SetProperty(ref _transientDuration, value);
    }

    public IslandActivity? CurrentActivity
    {
        get => _currentActivity;
        protected set
        {
            if (SetProperty(ref _currentActivity, value))
            {
                NotifyChanged();
            }
        }
    }

    public WidgetDisplayMode DisplayMode
    {
        get => _displayMode;
        internal set => SetProperty(ref _displayMode, value);
    }

    public bool IsVisibleOnIsland
    {
        get => _isVisibleOnIsland;
        protected set => SetProperty(ref _isVisibleOnIsland, value);
    }

    /// <summary>
    /// Updates the display state and visibility. Subclasses can override to manage resource timers.
    /// </summary>
    public virtual void SetDisplayState(WidgetDisplayMode mode, bool isVisible)
    {
        DisplayMode = mode;
        IsVisibleOnIsland = isVisible;
    }

    public event EventHandler? Changed;

    protected IslandWidgetBase(int initialPriority = ActivityPriority.Normal)
    {
        _priority = initialPriority;
    }

    /// <summary>
    /// Activates the widget with optional transient duration and priority override.
    /// </summary>
    public virtual void Activate(TimeSpan? transientDuration = null, int? priorityOverride = null)
    {
        if (priorityOverride.HasValue)
        {
            _priority = priorityOverride.Value;
        }

        _isTransient = transientDuration.HasValue;
        _transientDuration = transientDuration;
        _lastActivatedUtc = DateTimeOffset.UtcNow;
        _isActive = true;

        OnPropertyChanged(nameof(Priority));
        OnPropertyChanged(nameof(IsTransient));
        OnPropertyChanged(nameof(TransientDuration));
        OnPropertyChanged(nameof(LastActivatedUtc));
        OnPropertyChanged(nameof(IsActive));

        NotifyChanged();
    }

    /// <summary>
    /// Deactivates the widget.
    /// </summary>
    public virtual void Deactivate()
    {
        if (!_isActive) return;

        _isActive = false;
        _isTransient = false;
        _transientDuration = null;

        OnPropertyChanged(nameof(IsActive));
        OnPropertyChanged(nameof(IsTransient));
        OnPropertyChanged(nameof(TransientDuration));

        NotifyChanged();
    }

    /// <summary>
    /// Fires the <see cref="Changed"/> event and publishes <see cref="ActivityChangedMessage"/> via <see cref="WeakReferenceMessenger"/>.
    /// </summary>
    protected void NotifyChanged()
    {
        Changed?.Invoke(this, EventArgs.Empty);
        WeakReferenceMessenger.Default.Send(new ActivityChangedMessage(this));
    }

    public abstract UserControl? CreateCompactView();

    public abstract UserControl? CreateExpandedView();

    public virtual UserControl? CreateSplitView() => CreateCompactView();

    public virtual void Initialize()
    {
    }

    public virtual void OnExpand()
    {
    }

    public virtual void OnCollapse()
    {
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_isDisposed)
        {
            _isDisposed = true;
            Changed = null;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
}
