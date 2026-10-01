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
    private ScaleTransform[]? _transforms;
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
        _transforms ??=
        [
            (ScaleTransform)Bar0.RenderTransform,
            (ScaleTransform)Bar1.RenderTransform,
            (ScaleTransform)Bar2.RenderTransform,
            (ScaleTransform)Bar3.RenderTransform,
            (ScaleTransform)Bar4.RenderTransform,
            (ScaleTransform)Bar5.RenderTransform,
            (ScaleTransform)Bar6.RenderTransform,
            (ScaleTransform)Bar7.RenderTransform,
            (ScaleTransform)Bar8.RenderTransform,
            (ScaleTransform)Bar9.RenderTransform,
            (ScaleTransform)Bar10.RenderTransform,
            (ScaleTransform)Bar11.RenderTransform
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
        if (!_isRenderingSubscribed || _transforms == null) return;

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
                // Dynamic GPU scale: [0.15, 1.0] -> Height: [2.1, 14.0] DIP without layout invalidation
                _transforms[i].ScaleY = Math.Clamp(0.15 + bands[i] * 0.85, 0.15, 1.0);
            }
        }
    }

    private void ResetBars()
    {
        if (_transforms == null) return;
        for (int i = 0; i < _transforms.Length; i++)
        {
            _transforms[i].ScaleY = 0.15;
        }
    }
}
