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
        _widget.PropertyChanged += OnWidgetPropertyChanged;
        _widget.GestureTriggered += OnGestureTriggered;
        UpdateProgressFill();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _widget.PropertyChanged -= OnWidgetPropertyChanged;
        _widget.GestureTriggered -= OnGestureTriggered;
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
