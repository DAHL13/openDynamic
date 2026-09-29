#if DEBUG
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using OpenDynamic.App.Animation;
using OpenDynamic.Core.State;

namespace OpenDynamic.App.Windowing;

/// <summary>
/// Debugging window for testing spring physics, state transitions, and rendering lifecycle.
/// STRICTLY conditioned to DEBUG builds (#if DEBUG). Zero presence in Release builds.
/// </summary>
public sealed class IslandDebugWindow : Window
{
    private readonly IslandAnimator _animator;
    private readonly TextBlock _statusBlock;
    private readonly TextBlock _dimensionsBlock;
    private readonly TextBlock _subscriptionBlock;
    private readonly Slider _stiffnessSlider;
    private readonly Slider _dampingSlider;
    private readonly TextBlock _stiffnessValueLabel;
    private readonly TextBlock _dampingValueLabel;

    public IslandDebugWindow(IslandAnimator animator)
    {
        _animator = animator ?? throw new ArgumentNullException(nameof(animator));

        Title = "openDynamic - Panel de Depuración (DEBUG)";
        Width = 460;
        Height = 560;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        Background = new SolidColorBrush(Color.FromRgb(24, 24, 27));
        Foreground = Brushes.White;

        var mainPanel = new StackPanel { Margin = new Thickness(16) };

        // Header
        mainPanel.Children.Add(new TextBlock
        {
            Text = "🛠 Monitor de Resortes y Estados (Fase 2)",
            FontSize = 16,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            Margin = new Thickness(0, 0, 0, 12)
        });

        // Telemetry Group
        var telemetryBorder = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(39, 39, 42)),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Margin = new Thickness(0, 0, 0, 16)
        };
        var telemetryStack = new StackPanel();

        _statusBlock = new TextBlock
        {
            Text = $"Estado FSM: {_animator.StateMachine.CurrentState}",
            FontWeight = FontWeights.SemiBold,
            FontSize = 14,
            Foreground = Brushes.LightGreen,
            Margin = new Thickness(0, 0, 0, 4)
        };
        telemetryStack.Children.Add(_statusBlock);

        _subscriptionBlock = new TextBlock
        {
            Text = "Render Loop: ○ EN REPOSO (0% CPU - Desuscrito)",
            FontSize = 12,
            Foreground = Brushes.LightGray,
            Margin = new Thickness(0, 0, 0, 4)
        };
        telemetryStack.Children.Add(_subscriptionBlock);

        _dimensionsBlock = new TextBlock
        {
            Text = $"Dimensiones: W:{_animator.CurrentDimensions.Width:F1} | H:{_animator.CurrentDimensions.Height:F1} | R:{_animator.CurrentDimensions.CornerRadius:F1} | Op:{_animator.CurrentDimensions.Opacity:F2}",
            FontFamily = new FontFamily("Consolas"),
            FontSize = 12,
            Foreground = Brushes.DarkGray
        };
        telemetryStack.Children.Add(_dimensionsBlock);
        telemetryBorder.Child = telemetryStack;
        mainPanel.Children.Add(telemetryBorder);

        // State Transition Buttons Group
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Forzar Transición de Estado:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var statesGrid = new UniformGrid { Columns = 2, Margin = new Thickness(0, 0, 0, 16) };

        statesGrid.Children.Add(CreateButton("Compact (160x36)", () => _animator.AnimateTo(IslandState.Compact)));
        statesGrid.Children.Add(CreateButton("Expanded (400x160)", () =>
        {
            // If hidden, transition through compact first as per FSM rules
            if (_animator.StateMachine.CurrentState == IslandState.Hidden)
            {
                _animator.StateMachine.TryTransitionTo(IslandState.Compact);
            }
            _animator.AnimateTo(IslandState.Expanded);
        }));
        statesGrid.Children.Add(CreateButton("Split (260x36)", () =>
        {
            if (_animator.StateMachine.CurrentState == IslandState.Hidden)
            {
                _animator.StateMachine.TryTransitionTo(IslandState.Compact);
            }
            _animator.AnimateTo(IslandState.Split);
        }));
        statesGrid.Children.Add(CreateButton("Hidden (0x0)", () => _animator.AnimateTo(IslandState.Hidden)));

        mainPanel.Children.Add(statesGrid);

        // Spring Controls Group
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Afinación de Física de Resorte:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        // Initialize sliders and labels before wiring event handlers
        _stiffnessValueLabel = new TextBlock
        {
            Text = $"{_animator.WidthSpring.Stiffness:F0}",
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = FontWeights.Bold
        };

        _stiffnessSlider = new Slider
        {
            Minimum = 50,
            Maximum = 800,
            Value = _animator.WidthSpring.Stiffness,
            Margin = new Thickness(0, 0, 0, 12)
        };

        _dampingValueLabel = new TextBlock
        {
            Text = $"{_animator.WidthSpring.Damping:F0}",
            HorizontalAlignment = HorizontalAlignment.Right,
            FontWeight = FontWeights.Bold
        };

        _dampingSlider = new Slider
        {
            Minimum = 5,
            Maximum = 80,
            Value = _animator.WidthSpring.Damping,
            Margin = new Thickness(0, 0, 0, 16)
        };

        // Wire handlers now that both sliders are allocated
        _stiffnessSlider.ValueChanged += (s, e) =>
        {
            _stiffnessValueLabel.Text = $"{e.NewValue:F0}";
            _animator.SetSpringParameters(e.NewValue, _dampingSlider.Value);
        };

        _dampingSlider.ValueChanged += (s, e) =>
        {
            _dampingValueLabel.Text = $"{e.NewValue:F0}";
            _animator.SetSpringParameters(_stiffnessSlider.Value, e.NewValue);
        };

        // Stiffness UI layout
        var stiffnessHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        stiffnessHeader.Children.Add(new TextBlock { Text = "Rigidez (Stiffness / k):", HorizontalAlignment = HorizontalAlignment.Left });
        stiffnessHeader.Children.Add(_stiffnessValueLabel);
        mainPanel.Children.Add(stiffnessHeader);
        mainPanel.Children.Add(_stiffnessSlider);

        // Damping UI layout
        var dampingHeader = new Grid { Margin = new Thickness(0, 0, 0, 4) };
        dampingHeader.Children.Add(new TextBlock { Text = "Amortiguamiento (Damping / c):", HorizontalAlignment = HorizontalAlignment.Left });
        dampingHeader.Children.Add(_dampingValueLabel);
        mainPanel.Children.Add(dampingHeader);
        mainPanel.Children.Add(_dampingSlider);

        // Presets
        mainPanel.Children.Add(new TextBlock
        {
            Text = "Perfiles Preconfigurados:",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });

        var presetsGrid = new UniformGrid { Columns = 3, Margin = new Thickness(0, 0, 0, 16) };
        presetsGrid.Children.Add(CreateButton("iOS Fluido", () => ApplyPreset(320, 26)));
        presetsGrid.Children.Add(CreateButton("Rebote Vivo", () => ApplyPreset(420, 20)));
        presetsGrid.Children.Add(CreateButton("Amortiguado", () => ApplyPreset(300, 36)));
        mainPanel.Children.Add(presetsGrid);

        Content = new ScrollViewer { Content = mainPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        // Hook animator events to update live telemetry
        _animator.FrameUpdated += OnAnimatorUpdated;
        _animator.Settled += OnAnimatorUpdated;
        _animator.Started += OnAnimatorUpdated;
    }

    private void ApplyPreset(double stiffness, double damping)
    {
        _stiffnessSlider.Value = stiffness;
        _dampingSlider.Value = damping;
        _animator.SetSpringParameters(stiffness, damping);
    }

    private void OnAnimatorUpdated(object? sender, EventArgs e)
    {
        Dispatcher.InvokeAsync(() =>
        {
            _statusBlock.Text = $"Estado FSM: {_animator.StateMachine.CurrentState}";
            _subscriptionBlock.Text = _animator.IsSubscribed
                ? "Render Loop: ● ACTIVO (Renderizando resortes)"
                : "Render Loop: ○ EN REPOSO (0% CPU - Desuscrito)";
            _subscriptionBlock.Foreground = _animator.IsSubscribed
                ? Brushes.Gold
                : Brushes.LightGreen;

            var d = _animator.CurrentDimensions;
            _dimensionsBlock.Text = $"Dimensiones: W:{d.Width:F1} | H:{d.Height:F1} | R:{d.CornerRadius:F1} | Op:{d.Opacity:F2}";
        });
    }

    private Button CreateButton(string label, Action action)
    {
        var btn = new Button
        {
            Content = label,
            Margin = new Thickness(4),
            Padding = new Thickness(8, 6, 8, 6),
            Background = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
            Foreground = Brushes.White,
            BorderThickness = new Thickness(0),
            Cursor = System.Windows.Input.Cursors.Hand
        };
        btn.Click += (_, _) => action();
        return btn;
    }

    protected override void OnClosed(EventArgs e)
    {
        _animator.FrameUpdated -= OnAnimatorUpdated;
        _animator.Settled -= OnAnimatorUpdated;
        _animator.Started -= OnAnimatorUpdated;
        base.OnClosed(e);
    }
}
#endif
