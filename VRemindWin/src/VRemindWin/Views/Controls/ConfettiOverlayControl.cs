using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.UI;

namespace VRemindWin.Views.Controls;

/// <summary>
/// DirectX 10 (D3D_FEATURE_LEVEL_10_0) hardware-accelerated confetti overlay.
/// Pre-allocates 45 geometry shapes once and updates only their GPU-backed
/// <see cref="CompositeTransform"/> properties on <see cref="CompositionTarget.Rendering"/> vsync ticks,
/// avoiding CPU layout passes (<c>Canvas.SetLeft</c> / <c>Canvas.SetTop</c>) and per-frame allocations.
/// </summary>
public sealed class ConfettiOverlayControl : Canvas
{
    private const int ParticleCount = 45;

    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 255, 0, 85),    // #FF0055
        Color.FromArgb(255, 255, 153, 0),   // #FF9900
        Color.FromArgb(255, 255, 230, 0),   // #FFE600
        Color.FromArgb(255, 51, 204, 51),   // #33CC33
        Color.FromArgb(255, 0, 191, 255),   // #00BFFF
        Color.FromArgb(255, 153, 51, 255)   // #9933FF
    ];

    private readonly struct ParticleSpec
    {
        public required CompositeTransform Transform { get; init; }
        public required double SeedX { get; init; }
        public required double SeedY { get; init; }
        public required double Speed { get; init; }
        public required double SwayAmplitude { get; init; }
        public required double SwayFrequency { get; init; }
        public required double RotationSpeed { get; init; }
        public required double PhaseOffset { get; init; }
    }

    private readonly ParticleSpec[] _particles = new ParticleSpec[ParticleCount];
    private readonly Stopwatch _fallbackClock = new();
    private bool _isInitialized;
    private bool _isActive;
    private bool _isRenderingSubscribed;

    public ConfettiOverlayControl()
    {
        IsHitTestVisible = false;
        Visibility = Visibility.Collapsed;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        SizeChanged += OnSizeChanged;
    }

    public void SetActive(bool isActive)
    {
        if (_isActive == isActive)
        {
            UpdateSubscriptionState();
            return;
        }

        _isActive = isActive;
        UpdateSubscriptionState();
    }

    private void EnsureParticlesInitialized()
    {
        if (_isInitialized)
        {
            return;
        }

        _isInitialized = true;
        var brushes = new SolidColorBrush[Palette.Length];
        for (int c = 0; c < Palette.Length; c++)
        {
            brushes[c] = new SolidColorBrush(Palette[c]);
        }

        for (int i = 0; i < ParticleCount; i++)
        {
            bool isCircle = i % 3 == 0;
            double width = isCircle ? 7.0 : 8.5;
            double height = isCircle ? 7.0 : 5.0;

            Shape shape = isCircle
                ? new Ellipse { Width = width, Height = height }
                : new Rectangle { Width = width, Height = height, RadiusX = 1.5, RadiusY = 1.5 };

            shape.Fill = brushes[i % brushes.Length];
            shape.Opacity = 0.82;
            shape.IsHitTestVisible = false;

            var transform = new CompositeTransform
            {
                CenterX = width * 0.5,
                CenterY = height * 0.5
            };
            shape.RenderTransform = transform;

            double rawX = Math.Abs(Math.Sin((i + 1) * 12.9898) * 43758.5453);
            double rawY = Math.Abs(Math.Cos((i + 1) * 78.233) * 43758.5453);

            _particles[i] = new ParticleSpec
            {
                Transform = transform,
                SeedX = rawX - Math.Floor(rawX),
                SeedY = rawY - Math.Floor(rawY),
                Speed = 38.0 + (i % 7) * 9.5,
                SwayAmplitude = 10.0 + (i % 5) * 4.5,
                SwayFrequency = 1.2 + (i % 4) * 0.45,
                RotationSpeed = (i % 2 == 0 ? 1.0 : -1.0) * (70.0 + (i % 6) * 28.0),
                PhaseOffset = i * 0.85
            };

            Children.Add(shape);
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        UpdateSubscriptionState();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        UnsubscribeRendering();
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateSubscriptionState();
    }

    private void UpdateSubscriptionState()
    {
        bool shouldRun = _isActive && IsLoaded;
        if (!shouldRun)
        {
            UnsubscribeRendering();
            Visibility = Visibility.Collapsed;
            return;
        }

        EnsureParticlesInitialized();
        Visibility = Visibility.Visible;

        if (!_isRenderingSubscribed)
        {
            _fallbackClock.Restart();
            CompositionTarget.Rendering += OnCompositionTargetRendering;
            _isRenderingSubscribed = true;
        }
    }

    private void UnsubscribeRendering()
    {
        if (_isRenderingSubscribed)
        {
            CompositionTarget.Rendering -= OnCompositionTargetRendering;
            _isRenderingSubscribed = false;
            _fallbackClock.Stop();
        }
    }

    private void OnCompositionTargetRendering(object? sender, object e)
    {
        if (!_isActive || !IsLoaded)
        {
            UnsubscribeRendering();
            return;
        }

        double width = ActualWidth > 1.0 ? ActualWidth : 340.0;
        double height = ActualHeight > 1.0 ? ActualHeight : 440.0;
        double totalVerticalSpan = height + 28.0;

        double elapsedSeconds = e is RenderingEventArgs renderingArgs
            ? renderingArgs.RenderingTime.TotalSeconds
            : _fallbackClock.Elapsed.TotalSeconds;

        for (int i = 0; i < ParticleCount; i++)
        {
            ref readonly var p = ref _particles[i];
            double y = ((p.SeedY * totalVerticalSpan + elapsedSeconds * p.Speed) % totalVerticalSpan) - 14.0;
            double x = (p.SeedX * width) + Math.Sin(elapsedSeconds * p.SwayFrequency + p.PhaseOffset) * p.SwayAmplitude;
            double angle = (elapsedSeconds * p.RotationSpeed + p.PhaseOffset * 40.0) % 360.0;

            p.Transform.TranslateX = x;
            p.Transform.TranslateY = y;
            p.Transform.Rotation = angle;
        }
    }
}
