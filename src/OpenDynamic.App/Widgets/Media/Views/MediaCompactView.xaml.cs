using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace OpenDynamic.App.Widgets.Media.Views;

/// <summary>
/// Interaction logic for MediaCompactView.xaml.
/// Features a 12-band logarithmic audio spectrum visualizer driven at ~30 FPS
/// via CompositionTarget.Rendering strictly when capture/visualization is active (Golden Rule 1).
/// </summary>
public partial class MediaCompactView : UserControl
{
    private readonly MediaWidget _widget;
    private Border[]? _bars;
    private bool _isRenderingSubscribed;
    private long _lastRenderTime;

    public MediaCompactView(MediaWidget widget)
    {
        _widget = widget ?? throw new ArgumentNullException(nameof(widget));
        DataContext = _widget;
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _bars ??=
        [
            Bar0, Bar1, Bar2, Bar3, Bar4, Bar5,
            Bar6, Bar7, Bar8, Bar9, Bar10, Bar11
        ];

        _widget.PropertyChanged += OnWidgetPropertyChanged;
        UpdateRenderingSubscription();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _widget.PropertyChanged -= OnWidgetPropertyChanged;
        StopRendering();
    }

    private void OnWidgetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaWidget.IsVisualizerActive)
            or nameof(MediaWidget.IsPlaying)
            or nameof(MediaWidget.IsVisibleOnIsland)
            or nameof(MediaWidget.EqualizerVisibility))
        {
            Dispatcher.InvokeAsync(UpdateRenderingSubscription);
        }
    }

    private void UpdateRenderingSubscription()
    {
        if (_widget.IsVisualizerActive && IsLoaded)
        {
            StartRendering();
        }
        else
        {
            StopRendering();
        }
    }

    private void StartRendering()
    {
        if (_isRenderingSubscribed) return;
        _isRenderingSubscribed = true;
        _lastRenderTime = 0;
        CompositionTarget.Rendering += OnRendering;
    }

    private void StopRendering()
    {
        if (!_isRenderingSubscribed) return;
        _isRenderingSubscribed = false;
        CompositionTarget.Rendering -= OnRendering;
        ResetBars();
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        if (!_isRenderingSubscribed || _bars == null) return;

        long now = Environment.TickCount64;
        // Throttle rendering to ~30 FPS (33 ms interval) to strictly respect performance budget
        if (now - _lastRenderTime < 33)
        {
            return;
        }
        _lastRenderTime = now;

        Span<float> bands = stackalloc float[12];
        var service = _widget.SpectrumService;
        if (service != null)
        {
            service.GetCompactBands(bands);

            for (int i = 0; i < 12; i++)
            {
                // Dynamic height: [2.0, 14.0] DIP
                _bars[i].Height = Math.Clamp(2.0 + bands[i] * 12.0, 2.0, 14.0);
            }
        }
    }

    private void ResetBars()
    {
        if (_bars == null) return;
        for (int i = 0; i < _bars.Length; i++)
        {
            _bars[i].Height = 2.0;
        }
    }
}
