using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CommunityToolkit.Mvvm.Messaging;
using OpenDynamic.App.Widgets.Messages;
using OpenDynamic.Core.Animation;
using OpenDynamic.Core.State;
using Serilog;

namespace OpenDynamic.App.Views;

/// <summary>
/// UserControl hosting the visual presentation of the Dynamic Island.
/// Manages the primary capsule, satellite circular bubble (Split mode),
/// and smooth opacity cross-fades during view transitions.
/// </summary>
public partial class IslandView : UserControl
{
    private const double SatelliteGap = 10.0;
    private const double SatelliteDiameter = 36.0;
    private const double SatelliteSpan = SatelliteGap + SatelliteDiameter; // 46 DIP

    private MotionProfile _currentMotionProfile = MotionProfile.Full;

    public Border CapsuleBorder => MainCapsuleBorder;
    public Border SatelliteBubble => SatelliteBorder;

    public IslandView()
    {
        InitializeComponent();

        WeakReferenceMessenger.Default.Register<MediaAccentColorChangedMessage>(this, (_, msg) =>
        {
            Dispatcher.InvokeAsync(() => ApplyAccentBorder(msg.AccentColor));
        });
    }

    /// <summary>
    /// Applies geometry, asymmetric corner radius (0, 0, cornerRadius, cornerRadius), clipping, and opacity
    /// to the notch and satellite bubble. Top corners are flat against the monitor bezel; bottom corners are rounded.
    /// The outer Border renders its background and outline stroke cleanly without clip interference, while
    /// the inner content containers are clipped to protect against child content overflow.
    /// </summary>
    public void ApplyDimensions(CapsuleDimensions dimensions, IslandState state)
    {
        double width = Math.Max(0.0, dimensions.Width);
        double height = Math.Max(0.0, dimensions.Height);
        double cornerRadius = Math.Max(0.0, dimensions.CornerRadius);
        double opacity = Math.Clamp(dimensions.Opacity, 0.0, 1.0);

        var notchCornerRadius = new CornerRadius(0, 0, cornerRadius, cornerRadius);

        // Leave outer Border.Clip null so BorderBrush stroke and Background render cleanly without clipping
        MainCapsuleBorder.Clip = null;
        SatelliteBorder.Clip = null;

        if (state == IslandState.Split)
        {
            // Split layout: main notch width adjusted so total span matches layout target
            double mainWidth = Math.Max(0.0, width - SatelliteSpan);
            MainCapsuleBorder.Width = mainWidth;
            MainCapsuleBorder.Height = height;
            MainCapsuleBorder.CornerRadius = notchCornerRadius;
            MainCapsuleBorder.Opacity = opacity;

            SatelliteBorder.Visibility = Visibility.Visible;
            SatelliteBorder.Opacity = opacity;
            SatelliteBorder.CornerRadius = notchCornerRadius;

            // Clip inner child containers to prevent widget content from overflowing rounded bottom corners
            double innerMainWidth = Math.Max(0.0, mainWidth - 2.0);
            double innerHeight = Math.Max(0.0, height - 1.0);
            double innerRadius = Math.Max(0.0, cornerRadius - 1.0);

            PrimaryContentContainer.Clip = innerMainWidth > 0.0 && innerHeight > 0.0
                ? CreateNotchClipGeometry(innerMainWidth, innerHeight, innerRadius)
                : null;

            double innerSatWidth = Math.Max(0.0, SatelliteDiameter - 2.0);
            SecondaryContentContainer.Clip = innerSatWidth > 0.0 && innerHeight > 0.0
                ? CreateNotchClipGeometry(innerSatWidth, innerHeight, innerRadius)
                : null;
        }
        else
        {
            MainCapsuleBorder.Width = width;
            MainCapsuleBorder.Height = height;
            MainCapsuleBorder.CornerRadius = notchCornerRadius;
            MainCapsuleBorder.Opacity = opacity;

            SatelliteBorder.Visibility = Visibility.Collapsed;
            SecondaryContentContainer.Clip = null;

            double innerWidth = Math.Max(0.0, width - 2.0);
            double innerHeight = Math.Max(0.0, height - 1.0);
            double innerRadius = Math.Max(0.0, cornerRadius - 1.0);

            PrimaryContentContainer.Clip = innerWidth > 0.0 && innerHeight > 0.0
                ? CreateNotchClipGeometry(innerWidth, innerHeight, innerRadius)
                : null;
        }
    }

    /// <summary>
    /// Creates an asymmetric clip geometry matching the notch form factor:
    /// flat at the top edge (0 DIP radius) and smoothly rounded at the bottom corners.
    /// </summary>
    private static Geometry? CreateNotchClipGeometry(double width, double height, double cornerRadius)
    {
        if (width <= 0.0 || height <= 0.0)
        {
            return null;
        }

        double r = Math.Min(cornerRadius, Math.Min(width / 2.0, height));
        if (r <= 0.0)
        {
            var rectGeom = new RectangleGeometry(new Rect(0, 0, width, height));
            rectGeom.Freeze();
            return rectGeom;
        }

        var figure = new PathFigure
        {
            StartPoint = new Point(0, 0),
            IsClosed = true,
            IsFilled = true
        };

        // Top edge: flat from (0,0) to (width,0)
        figure.Segments.Add(new LineSegment(new Point(width, 0), isStroked: false));

        // Right edge: down to bottom-right corner start
        figure.Segments.Add(new LineSegment(new Point(width, height - r), isStroked: false));

        // Bottom-right corner: curved arc
        figure.Segments.Add(new ArcSegment(new Point(width - r, height), new Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: false));

        // Bottom edge: flat from bottom-right corner to bottom-left corner
        figure.Segments.Add(new LineSegment(new Point(r, height), isStroked: false));

        // Bottom-left corner: curved arc
        figure.Segments.Add(new ArcSegment(new Point(0, height - r), new Size(r, r), 0, false, SweepDirection.Clockwise, isStroked: false));

        // Left edge is closed automatically back up to (0,0)

        var geometry = new PathGeometry();
        geometry.Figures.Add(figure);
        geometry.Freeze();
        return geometry;
    }

    /// <summary>
    /// Transitions and displays the specified primary and secondary views with a short opacity cross-fade.
    /// </summary>
    public void PresentViews(UserControl? primaryView, UserControl? secondaryView, IslandState state)
    {
        TransitionContent(PrimaryContent, primaryView);

        if (state == IslandState.Split && secondaryView != null)
        {
            TransitionContent(SecondaryContent, secondaryView);
            SatelliteBorder.Visibility = Visibility.Visible;
        }
        else
        {
            TransitionContent(SecondaryContent, null);
            SatelliteBorder.Visibility = Visibility.Collapsed;
        }
    }

    /// <summary>
    /// Updates the motion profile used for cross-fade transitions and visual dynamics.
    /// </summary>
    public void UpdateMotionProfile(MotionProfile profile)
    {
        _currentMotionProfile = profile ?? MotionProfile.Full;
    }

    /// <summary>
    /// Smoothly swaps content on a ContentControl using opacity cross-fade according to the active motion profile.
    /// In reduced mode (&lt;=150ms total), uses accelerated, direct transitions.
    /// </summary>
    private void TransitionContent(ContentControl container, UserControl? newContent)
    {
        if (ReferenceEquals(container.Content, newContent))
        {
            return;
        }

        int outMs = _currentMotionProfile.CrossFadeOutDurationMs;
        int inMs = _currentMotionProfile.CrossFadeInDurationMs;

        if (outMs <= 0 && inMs <= 0)
        {
            container.BeginAnimation(OpacityProperty, null);
            container.Content = newContent;
            container.Opacity = 1.0;
            return;
        }

        if (container.Content == null)
        {
            // First time presenting: fade in directly
            container.Content = newContent;
            if (newContent != null)
            {
                var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(inMs));
                container.BeginAnimation(OpacityProperty, fadeIn);
            }
            return;
        }

        if (newContent == null)
        {
            // Fading out
            var fadeOut = new DoubleAnimation(container.Opacity, 0.0, TimeSpan.FromMilliseconds(outMs));
            fadeOut.Completed += (_, _) =>
            {
                container.Content = null;
                container.Opacity = 1.0;
            };
            container.BeginAnimation(OpacityProperty, fadeOut);
            return;
        }

        // Fade out current, swap, and fade in new
        var crossFadeOut = new DoubleAnimation(container.Opacity, 0.0, TimeSpan.FromMilliseconds(outMs));
        crossFadeOut.Completed += (_, _) =>
        {
            container.Content = newContent;
            var crossFadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(inMs));
            container.BeginAnimation(OpacityProperty, crossFadeIn);
        };
        container.BeginAnimation(OpacityProperty, crossFadeOut);
    }

    private Color? _currentAccentColor;

    /// <summary>
    /// Applies a subtle dynamic accent color to the notch outline border.
    /// In standard motion mode, animates color with a short ColorAnimation (~300ms) only when changing tracks.
    /// In reduced motion mode, applies the color cut instantaneously.
    /// When null, resets the border brush to the theme default.
    /// Strictly adheres to performance budget (zero large DropShadowEffects).
    /// </summary>
    public void ApplyAccentBorder(Color? accentColor)
    {
        if (_currentAccentColor == accentColor) return;
        _currentAccentColor = accentColor;

        if (accentColor == null)
        {
            MainCapsuleBorder.BeginAnimation(Border.BorderBrushProperty, null);
            MainCapsuleBorder.ClearValue(Border.BorderBrushProperty);
            SatelliteBorder.ClearValue(Border.BorderBrushProperty);
            return;
        }

        var targetColor = accentColor.Value;

        if (!_currentMotionProfile.AllowDecorative)
        {
            // Reduced motion mode: instantaneous color cut
            var frozenBrush = new SolidColorBrush(targetColor);
            frozenBrush.Freeze();
            MainCapsuleBorder.BorderBrush = frozenBrush;
            SatelliteBorder.BorderBrush = frozenBrush;
            return;
        }

        // Standard animated transition (~300ms)
        var startColor = (MainCapsuleBorder.BorderBrush as SolidColorBrush)?.Color ?? Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
        var animBrush = new SolidColorBrush(startColor);
        MainCapsuleBorder.BorderBrush = animBrush;
        SatelliteBorder.BorderBrush = animBrush;

        var colorAnim = new ColorAnimation
        {
            To = targetColor,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        animBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
    }
}
