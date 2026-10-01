using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using OpenDynamic.Core.Media.Gestures;

namespace OpenDynamic.App.Widgets.Media.Views;

public partial class MediaExpandedView : UserControl
{
    private readonly MediaWidget _widget;
    private bool _isDraggingHeader;
    private Point _dragStartPoint;
    private Border[]? _spectrumBars;
    private bool _isRenderingSubscribed;
    private long _lastRenderTime;

    public MediaExpandedView(MediaWidget widget)
    {
        _widget = widget ?? throw new ArgumentNullException(nameof(widget));
        DataContext = _widget;
        InitializeComponent();

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _spectrumBars ??=
        [
            ExpBar0, ExpBar1, ExpBar2, ExpBar3, ExpBar4, ExpBar5,
            ExpBar6, ExpBar7, ExpBar8, ExpBar9, ExpBar10, ExpBar11,
            ExpBar12, ExpBar13, ExpBar14, ExpBar15, ExpBar16, ExpBar17,
            ExpBar18, ExpBar19, ExpBar20, ExpBar21, ExpBar22, ExpBar23
        ];

        _widget.PropertyChanged += OnWidgetPropertyChanged;
        _widget.GestureTriggered += OnGestureTriggered;
        UpdateProgressFill();
        UpdateRenderingSubscription();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _widget.PropertyChanged -= OnWidgetPropertyChanged;
        _widget.GestureTriggered -= OnGestureTriggered;
        StopRendering();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateProgressFill();
    }

    private void OnWidgetPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MediaWidget.ProgressRatio) or nameof(MediaWidget.CurrentPosition))
        {
            Dispatcher.InvokeAsync(UpdateProgressFill);
        }
        else if (e.PropertyName is nameof(MediaWidget.IsVisualizerActive)
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
        if (!_isRenderingSubscribed || _spectrumBars == null) return;

        long now = Environment.TickCount64;
        // Limit rendering to ~30 FPS (33 ms interval)
        if (now - _lastRenderTime < 33)
        {
            return;
        }
        _lastRenderTime = now;

        Span<float> bands = stackalloc float[24];
        var service = _widget.SpectrumService;
        if (service != null)
        {
            service.GetExpandedBands(bands);

            for (int i = 0; i < 24; i++)
            {
                // Dynamic height: [3.0, 16.0] DIP
                _spectrumBars[i].Height = Math.Clamp(3.0 + bands[i] * 13.0, 3.0, 16.0);
            }
        }
    }

    private void ResetBars()
    {
        if (_spectrumBars == null) return;
        for (int i = 0; i < _spectrumBars.Length; i++)
        {
            _spectrumBars[i].Height = 3.0;
        }
    }

    private void UpdateProgressFill()
    {
        var totalWidth = ProgressBarContainer.ActualWidth;
        if (totalWidth <= 0) return;

        var ratio = Math.Clamp(_widget.ProgressRatio, 0.0, 1.0);
        ActiveProgressFill.Width = totalWidth * ratio;
    }

    #region Horizontal Gesture Dragging and Spring Feedback (Phase 13)

    private void OnArtworkHeaderMouseDown(object sender, MouseButtonEventArgs e)
    {
        // Ignore if user clicked directly on child button (e.g. ActivateApp button)
        if (e.OriginalSource is DependencyObject dep && FindVisualParent<Button>(dep) != null)
        {
            return;
        }

        _isDraggingHeader = true;
        _dragStartPoint = e.GetPosition(this);
        ArtworkHeaderContainer.CaptureMouse();
        e.Handled = true;
    }

    private void OnArtworkHeaderMouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDraggingHeader) return;

        var currentPos = e.GetPosition(this);
        double deltaX = currentPos.X - _dragStartPoint.X;

        if (_widget.IsDecorativeAllowed)
        {
            // Soft elastic displacement during drag
            ArtworkTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            ArtworkTranslate.X = Math.Clamp(deltaX * 0.35, -28.0, 28.0);
        }
    }

    private void OnArtworkHeaderMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDraggingHeader) return;

        _isDraggingHeader = false;
        ArtworkHeaderContainer.ReleaseMouseCapture();

        var currentPos = e.GetPosition(this);
        double deltaX = currentPos.X - _dragStartPoint.X;

        bool triggered = _widget.HandleDragDelta(deltaX);
        if (!triggered)
        {
            AnimateSpringReturn();
        }

        e.Handled = true;
    }

    private void OnArtworkHeaderMouseLeave(object sender, MouseEventArgs e)
    {
        if (_isDraggingHeader && e.LeftButton == MouseButtonState.Released)
        {
            _isDraggingHeader = false;
            ArtworkHeaderContainer.ReleaseMouseCapture();
            AnimateSpringReturn();
        }
    }

    private void OnGestureTriggered(object? sender, SwipeGestureAction action)
    {
        Dispatcher.InvokeAsync(() =>
        {
            if (!_widget.IsDecorativeAllowed)
            {
                ArtworkTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                ArtworkTranslate.X = 0;
                return;
            }

            double bounceTarget = action == SwipeGestureAction.Next ? -18.0 : 18.0;
            TriggerSpringBounce(bounceTarget);
        });
    }

    private void TriggerSpringBounce(double targetOffset)
    {
        ArtworkTranslate.BeginAnimation(TranslateTransform.XProperty, null);
        ArtworkTranslate.X = targetOffset;

        var springAnim = new DoubleAnimation(targetOffset, 0.0, TimeSpan.FromMilliseconds(350))
        {
            EasingFunction = new ElasticEase
            {
                Oscillations = 1,
                Springiness = 3,
                EasingMode = EasingMode.EaseOut
            }
        };

        ArtworkTranslate.BeginAnimation(TranslateTransform.XProperty, springAnim);
    }

    private void AnimateSpringReturn()
    {
        if (!_widget.IsDecorativeAllowed)
        {
            ArtworkTranslate.BeginAnimation(TranslateTransform.XProperty, null);
            ArtworkTranslate.X = 0;
            return;
        }

        var springAnim = new DoubleAnimation(ArtworkTranslate.X, 0.0, TimeSpan.FromMilliseconds(300))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        ArtworkTranslate.BeginAnimation(TranslateTransform.XProperty, springAnim);
    }

    private static T? FindVisualParent<T>(DependencyObject child) where T : DependencyObject
    {
        DependencyObject? parentObject = VisualTreeHelper.GetParent(child);
        if (parentObject == null) return null;
        if (parentObject is T parent) return parent;
        return FindVisualParent<T>(parentObject);
    }

    #endregion

    #region Progress Seek Bar (Isolated from header gestures)

    private void OnProgressTrackClicked(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true;
        SeekFromMouse(e.GetPosition(ProgressBarContainer));
    }

    private void OnProgressTrackMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
        {
            e.Handled = true;
            SeekFromMouse(e.GetPosition(ProgressBarContainer));
        }
    }

    private void SeekFromMouse(Point pt)
    {
        var totalWidth = ProgressBarContainer.ActualWidth;
        if (totalWidth <= 0) return;

        var ratio = Math.Clamp(pt.X / totalWidth, 0.0, 1.0);
        ActiveProgressFill.Width = totalWidth * ratio;
        _ = _widget.SeekToRatioAsync(ratio);
    }

    #endregion
}
