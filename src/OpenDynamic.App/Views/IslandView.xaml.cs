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
    private bool _isMicrophoneActive;
    private bool _isCameraActive;
    private bool _hasSecondarySplitView;
    private bool _isSatelliteFadingOut;
    private IslandState _currentState = IslandState.Compact;
    private CapsuleDimensions _currentDimensions = new(200, 36, 14, 1.0);

    public Border CapsuleBorder => MainCapsuleBorder;
    public Border SatelliteBubble => SatelliteBorder;

    /// <summary>
    /// Indicates whether the satellite capsule is currently performing an animated exit fade-out.
    /// </summary>
    public bool IsSatelliteFadingOut => _isSatelliteFadingOut;

    /// <summary>
    /// Occurs when the satellite capsule exit fade-out animation completes.
    /// </summary>
    public event EventHandler? SatelliteFadeOutCompleted;

    public IslandView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Applies geometry, asymmetric corner radius (0, 0, cornerRadius, cornerRadius), clipping, and opacity
    /// to the notch and satellite bubble. Top corners are flat against the monitor bezel; bottom corners are rounded.
    /// The outer Border renders its background and outline stroke cleanly without clip interference, while
    /// the inner content containers are clipped to protect against child content overflow.
    /// </summary>
    public void ApplyDimensions(CapsuleDimensions dimensions, IslandState state)
    {
        _currentState = state;
        _currentDimensions = dimensions;

        double width = Math.Max(0.0, dimensions.Width);
        double height = Math.Max(0.0, dimensions.Height);
        double cornerRadius = Math.Max(0.0, dimensions.CornerRadius);
        double opacity = Math.Clamp(dimensions.Opacity, 0.0, 1.0);

        var notchCornerRadius = new CornerRadius(0, 0, cornerRadius, cornerRadius);

        // Enforce strict top bezel anchoring: vertical translation (Y) must NEVER exceed 0 DIP.
        // Prevents spring oscillation during retraction from pushing the notch downwards and exposing the desktop background.
        double targetY = CapsuleTranslate?.Y ?? 0.0;
        double clampedY = Math.Min(0.0, targetY);
        if (CapsuleTranslate != null)
        {
            CapsuleTranslate.Y = clampedY;
        }
        if (SatelliteTranslate != null)
        {
            SatelliteTranslate.Y = Math.Min(0.0, SatelliteTranslate.Y);
        }

        // Leave outer Border.Clip null so BorderBrush stroke and Background render cleanly without clipping
        MainCapsuleBorder.Clip = null;
        SatelliteBorder.Clip = null;

        if (state == IslandState.Hidden)
        {
            MainCapsuleBorder.Width = width;
            MainCapsuleBorder.Height = height;
            MainCapsuleBorder.CornerRadius = notchCornerRadius;
            MainCapsuleBorder.Opacity = opacity;

            PrimaryContentContainer.Clip = null;
            UpdateSatelliteLayout();
            return;
        }

        if (state == IslandState.Split)
        {
            // Split layout: main notch width adjusted so total span matches layout target
            double mainWidth = Math.Max(0.0, width - SatelliteSpan);
            MainCapsuleBorder.Width = mainWidth;
        }
        else
        {
            MainCapsuleBorder.Width = width;
        }

        MainCapsuleBorder.Height = height;
        MainCapsuleBorder.CornerRadius = notchCornerRadius;
        MainCapsuleBorder.Opacity = opacity;

        // Clip inner child containers to prevent widget content from overflowing rounded bottom corners
        double innerMainWidth = Math.Max(0.0, MainCapsuleBorder.Width - 2.0);
        double innerHeight = Math.Max(0.0, height - 1.0);
        double innerRadius = Math.Max(0.0, cornerRadius - 1.0);

        PrimaryContentContainer.Clip = innerMainWidth > 0.0 && innerHeight > 0.0
            ? CreateNotchClipGeometry(innerMainWidth, innerHeight, innerRadius)
            : null;

        UpdateSatelliteLayout();
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
        _currentState = state;
        _hasSecondarySplitView = state == IslandState.Split && secondaryView != null;

        TransitionContent(PrimaryContent, primaryView);

        if (_hasSecondarySplitView)
        {
            TransitionContent(SecondaryContent, secondaryView);
        }
        else
        {
            TransitionContent(SecondaryContent, null);
        }

        UpdateSatelliteLayout();
    }

    /// <summary>
    /// Updates the persistent privacy sensor indicator dots displayed in the satellite capsule.
    /// Amber (#FFFF9500) for microphone, green (#34C759) for camera.
    /// When active and the island is not hidden, shows SatelliteBorder strictly locked to 36 DIP height.
    /// </summary>
    public void UpdatePrivacyIndicators(bool isMicrophoneActive, bool isCameraActive)
    {
        _isMicrophoneActive = isMicrophoneActive;
        _isCameraActive = isCameraActive;

        UpdateSatelliteLayout();
    }

    /// <summary>
    /// Updates the satellite capsule layout, dimensions, and child presentation.
    /// Satellite height is strictly locked at 36 DIP (SatelliteDiameter), completely decoupled
    /// from the main notch animations (Expanded ~160 DIP, Compact 36 DIP).
    /// In Split mode with privacy indicators active, dynamically widens to 56 DIP to place dots
    /// to the right of the secondary widget (CPU/Timer) with zero overlap.
    /// </summary>
    private void UpdateSatelliteLayout()
    {
        bool hasActivePrivacy = _isCameraActive || _isMicrophoneActive;
        bool isSplitMode = _currentState == IslandState.Split && _hasSecondarySplitView;

        if (hasActivePrivacy || isSplitMode)
        {
            // Sensors reactivated or split mode active: cancel any in-flight exit animation
            if (_isSatelliteFadingOut)
            {
                CancelSatelliteFadeOut();
            }

            // Deterministic dot visibility
            MicrophoneIndicatorDot.Visibility = _isMicrophoneActive ? Visibility.Visible : Visibility.Collapsed;
            CameraIndicatorDot.Visibility = _isCameraActive ? Visibility.Visible : Visibility.Collapsed;

            // Strictly lock satellite height and corner radius (0,0,14,14) - NEVER inherit from main notch!
            SatelliteBorder.Height = SatelliteDiameter;
            SatelliteBorder.MinHeight = SatelliteDiameter;
            SatelliteBorder.MaxHeight = SatelliteDiameter;
            SatelliteBorder.CornerRadius = new CornerRadius(0, 0, 14, 14);
            SatelliteBorder.Opacity = _currentState == IslandState.Hidden ? 1.0 : _currentDimensions.Opacity;

            if (isSplitMode)
            {
                SecondaryContent.Visibility = Visibility.Visible;

                if (hasActivePrivacy)
                {
                    // Coexistence: widen satellite to 56 DIP to place dots to the right of secondary view without overlap
                    SatelliteBorder.Width = 56.0;
                    PrivacySatellitePanel.Visibility = Visibility.Visible;
                    PrivacySatellitePanel.Margin = new Thickness(0, 0, 4, 0);

                    double innerSatWidth = 54.0;
                    SecondaryContentContainer.Clip = CreateNotchClipGeometry(innerSatWidth, 35.0, 13.0);
                }
                else
                {
                    SatelliteBorder.Width = SatelliteDiameter;
                    PrivacySatellitePanel.Visibility = Visibility.Collapsed;
                    PrivacySatellitePanel.Margin = new Thickness(0);

                    double innerSatWidth = SatelliteDiameter - 2.0;
                    SecondaryContentContainer.Clip = CreateNotchClipGeometry(innerSatWidth, 35.0, 13.0);
                }

                SatelliteBorder.Visibility = Visibility.Visible;
            }
            else
            {
                SecondaryContent.Visibility = Visibility.Collapsed;

                SatelliteBorder.Width = SatelliteDiameter;
                PrivacySatellitePanel.Visibility = Visibility.Visible;
                PrivacySatellitePanel.Margin = new Thickness(0);
                SatelliteBorder.Visibility = Visibility.Visible;

                double innerSatWidth = SatelliteDiameter - 2.0;
                SecondaryContentContainer.Clip = CreateNotchClipGeometry(innerSatWidth, 35.0, 13.0);
            }
        }
        else
        {
            // Sensors inactive and not in split mode: satellite should hide
            SecondaryContent.Visibility = Visibility.Collapsed;

            if (SatelliteBorder.Visibility == Visibility.Visible && !_isSatelliteFadingOut)
            {
                // Smooth fade-out exit animation (~180ms with QuadraticEase)
                StartSatelliteFadeOut();
            }
            else if (!_isSatelliteFadingOut)
            {
                SatelliteBorder.Visibility = Visibility.Collapsed;
                PrivacySatellitePanel.Visibility = Visibility.Collapsed;
                MicrophoneIndicatorDot.Visibility = Visibility.Collapsed;
                CameraIndicatorDot.Visibility = Visibility.Collapsed;
                SecondaryContentContainer.Clip = null;
            }
        }
    }

    /// <summary>
    /// Starts a smooth opacity fade-out animation on the satellite capsule (~180ms QuadraticEase).
    /// Upon completion, collapses the satellite and resets opacity to 1.0.
    /// </summary>
    private void StartSatelliteFadeOut()
    {
        if (_isSatelliteFadingOut) return;
        _isSatelliteFadingOut = true;

        if (_currentMotionProfile.CrossFadeOutDurationMs <= 0)
        {
            _isSatelliteFadingOut = false;
            SatelliteBorder.BeginAnimation(OpacityProperty, null);
            SatelliteBorder.Visibility = Visibility.Collapsed;
            SatelliteBorder.Opacity = 1.0;
            PrivacySatellitePanel.Visibility = Visibility.Collapsed;
            MicrophoneIndicatorDot.Visibility = Visibility.Collapsed;
            CameraIndicatorDot.Visibility = Visibility.Collapsed;
            SecondaryContent.Visibility = Visibility.Collapsed;
            SecondaryContentContainer.Clip = null;
            SatelliteFadeOutCompleted?.Invoke(this, EventArgs.Empty);
            return;
        }

        var fadeOut = new DoubleAnimation
        {
            From = SatelliteBorder.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(180),
            EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
        };

        fadeOut.Completed += (s, e) =>
        {
            if (_isSatelliteFadingOut)
            {
                _isSatelliteFadingOut = false;
                SatelliteBorder.BeginAnimation(OpacityProperty, null);

                bool currentHasActivePrivacy = _isCameraActive || _isMicrophoneActive;
                bool currentIsSplit = _currentState == IslandState.Split && _hasSecondarySplitView;

                if (!currentHasActivePrivacy && !currentIsSplit)
                {
                    SatelliteBorder.Visibility = Visibility.Collapsed;
                    PrivacySatellitePanel.Visibility = Visibility.Collapsed;
                    MicrophoneIndicatorDot.Visibility = Visibility.Collapsed;
                    CameraIndicatorDot.Visibility = Visibility.Collapsed;
                    SecondaryContent.Visibility = Visibility.Collapsed;
                    SecondaryContentContainer.Clip = null;
                }

                SatelliteBorder.Opacity = 1.0;
                SatelliteFadeOutCompleted?.Invoke(this, EventArgs.Empty);
            }
        };

        SatelliteBorder.BeginAnimation(OpacityProperty, fadeOut);
    }

    /// <summary>
    /// Cancels any running exit fade-out animation and restores full satellite opacity and visibility.
    /// </summary>
    private void CancelSatelliteFadeOut()
    {
        _isSatelliteFadingOut = false;
        SatelliteBorder.BeginAnimation(OpacityProperty, null);
        SatelliteBorder.Opacity = 1.0;
        SatelliteBorder.Visibility = Visibility.Visible;
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
    private bool _isAccentApplied;
    private SolidColorBrush? _animatingBrush;
    private ColorAnimation? _activeAnimation;
    private EventHandler? _activeAnimationCompleted;

    /// <summary>
    /// Applies a subtle dynamic accent color to the notch outline border.
    /// In standard motion mode, animates color with a short ColorAnimation (~300ms) only when changing tracks.
    /// In reduced motion mode, applies the color cut instantaneously.
    /// When null, smoothly resets the border brush to the theme default (AppBorderBrush) in ~150ms.
    /// Strictly adheres to performance budget (zero large DropShadowEffects) and guarantees border is never null/transparent.
    /// Always detaches animation clocks with BeginAnimation(..., null) so SetResourceReference takes full precedence.
    /// </summary>
    public void ApplyAccentBorder(Color? accentColor)
    {
        // Cancel and decouple any previous animation on Border.BorderBrushProperty (WPF priority rule)
        MainCapsuleBorder.BeginAnimation(Border.BorderBrushProperty, null);
        SatelliteBorder.BeginAnimation(Border.BorderBrushProperty, null);

        // Cancel and decouple any previous color animation running on brush
        if (_animatingBrush != null && _activeAnimation != null && _activeAnimationCompleted != null)
        {
            _activeAnimation.Completed -= _activeAnimationCompleted;
            _animatingBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
            _animatingBrush = null;
            _activeAnimation = null;
            _activeAnimationCompleted = null;
        }

        if (accentColor == null)
        {
            // If no accent is currently applied, immediately restore the system resource reference
            if (!_isAccentApplied)
            {
                MainCapsuleBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
                SatelliteBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
                return;
            }

            _isAccentApplied = false;
            _currentAccentColor = null;

            var defaultBrush = Application.Current?.TryFindResource("AppBorderBrush") as SolidColorBrush;
            var defaultColor = defaultBrush?.Color ?? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);

            // In reduced motion mode, restore instantaneously
            if (!_currentMotionProfile.AllowDecorative)
            {
                MainCapsuleBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
                SatelliteBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
                return;
            }

            var startColor = (MainCapsuleBorder.BorderBrush as SolidColorBrush)?.Color ?? defaultColor;
            if (startColor == defaultColor)
            {
                MainCapsuleBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
                SatelliteBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
                return;
            }

            var resetAnimBrush = new SolidColorBrush(startColor);
            MainCapsuleBorder.BorderBrush = resetAnimBrush;
            SatelliteBorder.BorderBrush = resetAnimBrush;

            var resetColorAnim = new ColorAnimation
            {
                From = startColor,
                To = defaultColor,
                Duration = TimeSpan.FromMilliseconds(150),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            EventHandler? onCompleted = null;
            onCompleted = (_, _) =>
            {
                resetColorAnim.Completed -= onCompleted;
                MainCapsuleBorder.BeginAnimation(Border.BorderBrushProperty, null);
                SatelliteBorder.BeginAnimation(Border.BorderBrushProperty, null);
                resetAnimBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);

                MainCapsuleBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");
                SatelliteBorder.SetResourceReference(Border.BorderBrushProperty, "AppBorderBrush");

                if (ReferenceEquals(_activeAnimation, resetColorAnim))
                {
                    _animatingBrush = null;
                    _activeAnimation = null;
                    _activeAnimationCompleted = null;
                }
            };

            resetColorAnim.Completed += onCompleted;
            _animatingBrush = resetAnimBrush;
            _activeAnimation = resetColorAnim;
            _activeAnimationCompleted = onCompleted;

            resetAnimBrush.BeginAnimation(SolidColorBrush.ColorProperty, resetColorAnim);
            return;
        }

        var targetColor = accentColor.Value;
        if (_isAccentApplied && _currentAccentColor == targetColor && MainCapsuleBorder.BorderBrush != null)
        {
            return;
        }

        _isAccentApplied = true;
        _currentAccentColor = targetColor;

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
        var defaultSystemColor = (Application.Current?.TryFindResource("AppBorderBrush") as SolidColorBrush)?.Color
                                 ?? Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF);
        var currentStartColor = (MainCapsuleBorder.BorderBrush as SolidColorBrush)?.Color ?? defaultSystemColor;
        var animBrush = new SolidColorBrush(currentStartColor);
        MainCapsuleBorder.BorderBrush = animBrush;
        SatelliteBorder.BorderBrush = animBrush;

        var colorAnim = new ColorAnimation
        {
            From = currentStartColor,
            To = targetColor,
            Duration = TimeSpan.FromMilliseconds(300),
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        EventHandler? onAnimCompleted = null;
        onAnimCompleted = (_, _) =>
        {
            colorAnim.Completed -= onAnimCompleted;
            MainCapsuleBorder.BeginAnimation(Border.BorderBrushProperty, null);
            SatelliteBorder.BeginAnimation(Border.BorderBrushProperty, null);
            animBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);

            var finalBrush = new SolidColorBrush(targetColor);
            finalBrush.Freeze();
            MainCapsuleBorder.BorderBrush = finalBrush;
            SatelliteBorder.BorderBrush = finalBrush;

            if (ReferenceEquals(_activeAnimation, colorAnim))
            {
                _animatingBrush = null;
                _activeAnimation = null;
                _activeAnimationCompleted = null;
            }
        };

        colorAnim.Completed += onAnimCompleted;
        _animatingBrush = animBrush;
        _activeAnimation = colorAnim;
        _activeAnimationCompleted = onAnimCompleted;

        animBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
    }
}
