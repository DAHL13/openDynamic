using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
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

    public Border CapsuleBorder => MainCapsuleBorder;
    public Border SatelliteBubble => SatelliteBorder;

    public IslandView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Applies geometry, corner radius, clipping, and opacity to the capsule and satellite bubble.
    /// </summary>
    public void ApplyDimensions(CapsuleDimensions dimensions, IslandState state)
    {
        double width = Math.Max(0.0, dimensions.Width);
        double height = Math.Max(0.0, dimensions.Height);
        double cornerRadius = Math.Max(0.0, dimensions.CornerRadius);
        double opacity = Math.Clamp(dimensions.Opacity, 0.0, 1.0);

        if (state == IslandState.Split)
        {
            // Split layout: main capsule width adjusted so total span matches layout target
            double mainWidth = Math.Max(0.0, width - SatelliteSpan);
            MainCapsuleBorder.Width = mainWidth;
            MainCapsuleBorder.Height = height;
            MainCapsuleBorder.CornerRadius = new CornerRadius(cornerRadius);
            MainCapsuleBorder.Opacity = opacity;

            SatelliteBorder.Visibility = Visibility.Visible;
            SatelliteBorder.Opacity = opacity;

            if (mainWidth > 0.0 && height > 0.0)
            {
                MainCapsuleBorder.Clip = new RectangleGeometry(
                    new Rect(0, 0, mainWidth, height),
                    cornerRadius,
                    cornerRadius);
            }
            else
            {
                MainCapsuleBorder.Clip = null;
            }
        }
        else
        {
            MainCapsuleBorder.Width = width;
            MainCapsuleBorder.Height = height;
            MainCapsuleBorder.CornerRadius = new CornerRadius(cornerRadius);
            MainCapsuleBorder.Opacity = opacity;

            SatelliteBorder.Visibility = Visibility.Collapsed;

            if (width > 0.0 && height > 0.0)
            {
                MainCapsuleBorder.Clip = new RectangleGeometry(
                    new Rect(0, 0, width, height),
                    cornerRadius,
                    cornerRadius);
            }
            else
            {
                MainCapsuleBorder.Clip = null;
            }
        }
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
    /// Smoothly swaps content on a ContentControl using a fast opacity fade (80ms).
    /// </summary>
    private static void TransitionContent(ContentControl container, UserControl? newContent)
    {
        if (ReferenceEquals(container.Content, newContent))
        {
            return;
        }

        if (container.Content == null)
        {
            // First time presenting: fade in directly
            container.Content = newContent;
            if (newContent != null)
            {
                var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(100));
                container.BeginAnimation(OpacityProperty, fadeIn);
            }
            return;
        }

        if (newContent == null)
        {
            // Fading out
            var fadeOut = new DoubleAnimation(container.Opacity, 0.0, TimeSpan.FromMilliseconds(80));
            fadeOut.Completed += (_, _) =>
            {
                container.Content = null;
                container.Opacity = 1.0;
            };
            container.BeginAnimation(OpacityProperty, fadeOut);
            return;
        }

        // Fade out current, swap, and fade in new
        var crossFadeOut = new DoubleAnimation(container.Opacity, 0.0, TimeSpan.FromMilliseconds(70));
        crossFadeOut.Completed += (_, _) =>
        {
            container.Content = newContent;
            var crossFadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(90));
            container.BeginAnimation(OpacityProperty, crossFadeIn);
        };
        container.BeginAnimation(OpacityProperty, crossFadeOut);
    }
}
