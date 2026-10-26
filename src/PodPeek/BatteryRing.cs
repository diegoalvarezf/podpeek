using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Path = System.Windows.Shapes.Path;

namespace PodPeek;

/// <summary>Un anillo de batería como el de macOS: icono en el centro, nombre y porcentaje debajo.</summary>
sealed class BatteryRing : StackPanel
{
    const double Size = 64, Stroke = 4.5;

    public static readonly DependencyProperty ProgressProperty = DependencyProperty.Register(
        nameof(Progress), typeof(double), typeof(BatteryRing),
        new PropertyMetadata(0.0, (d, _) => ((BatteryRing)d).DrawArc()));

    public double Progress
    {
        get => (double)GetValue(ProgressProperty);
        set => SetValue(ProgressProperty, value);
    }

    readonly Path arc = new() { StrokeThickness = Stroke, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
    readonly Path icon;
    readonly TextBlock value = new() { FontSize = 15, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center };
    readonly TextBlock bolt = new()
    {
        Text = "", // rayo (Segoe Fluent Icons / MDL2)
        FontFamily = new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets"),
        FontSize = 11, Margin = new Thickness(3, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center,
        Visibility = Visibility.Collapsed,
    };
    int? shown;

    public BatteryRing(string label, Geometry iconGeometry)
    {
        Width = 92;
        var ring = new Grid { Width = Size, Height = Size, Margin = new Thickness(0, 0, 0, 10) };
        var track = new Ellipse { StrokeThickness = Stroke };
        track.SetResourceReference(Shape.StrokeProperty, "TrackBrush");
        icon = new Path { Data = iconGeometry, Stretch = Stretch.Uniform, Width = 26, Height = 26 };
        icon.SetResourceReference(Shape.FillProperty, "IconBrush");
        ring.Children.Add(track);
        ring.Children.Add(arc);
        ring.Children.Add(icon);
        Children.Add(ring);

        var name = new TextBlock { Text = label, FontSize = 12, HorizontalAlignment = HorizontalAlignment.Center };
        name.SetResourceReference(TextBlock.ForegroundProperty, "SecondaryTextBrush");
        Children.Add(name);

        var row = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 2, 0, 0) };
        value.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        bolt.SetResourceReference(TextBlock.ForegroundProperty, "TextBrush");
        row.Children.Add(value);
        row.Children.Add(bolt);
        Children.Add(row);
        SetLevel(null, false, animate: false);
    }

    public void SetLevel(int? percent, bool charging, bool animate)
    {
        value.Text = percent is int p ? $"{p} %" : "—";
        bolt.Visibility = charging && percent is not null ? Visibility.Visible : Visibility.Collapsed;
        icon.Opacity = percent is null ? 0.35 : 1;
        arc.Stroke = new SolidColorBrush(Theme.ForLevel(percent ?? 100));

        double target = (percent ?? 0) / 100.0;
        if (animate && shown != percent)
        {
            BeginAnimation(ProgressProperty, new DoubleAnimation(target, TimeSpan.FromMilliseconds(700))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
            });
        }
        else if (!animate)
        {
            BeginAnimation(ProgressProperty, null);
            Progress = target;
        }
        shown = percent;
    }

    /// <summary>Vuelve a animar el anillo desde cero (al abrirse la ventana).</summary>
    public void Replay()
    {
        BeginAnimation(ProgressProperty, new DoubleAnimation(0, (shown ?? 0) / 100.0, TimeSpan.FromMilliseconds(700))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        });
    }

    void DrawArc()
    {
        double r = (Size - Stroke) / 2, c = Size / 2;
        double angle = Math.Clamp(Progress, 0, 0.9999) * 360;
        if (angle <= 0.01) { arc.Data = null; return; }
        double rad = (angle - 90) * Math.PI / 180;
        var start = new Point(c, c - r);
        var end = new Point(c + r * Math.Cos(rad), c + r * Math.Sin(rad));
        var figure = new PathFigure { StartPoint = start, IsClosed = false };
        figure.Segments.Add(new ArcSegment(end, new Size(r, r), 0, angle > 180, SweepDirection.Clockwise, true));
        arc.Data = new PathGeometry(new[] { figure });
    }

    // --- Iconos (vectoriales, en una caja de 28×28) ---

    public static Geometry LeftPod() => Pod(mirror: false);
    public static Geometry RightPod() => Pod(mirror: true);

    static Geometry Pod(bool mirror)
    {
        var head = new EllipseGeometry(new Point(11, 9), 7.5, 7.5);
        var stem = new RectangleGeometry(new Rect(13.5, 9, 5.5, 18), 2.75, 2.75);
        var pod = new CombinedGeometry(GeometryCombineMode.Union, head, stem);
        if (mirror) pod.Transform = new ScaleTransform(-1, 1, 14, 14);
        return pod;
    }

    public static Geometry Case()
    {
        var body = new RectangleGeometry(new Rect(2, 5, 24, 19), 7, 7);
        var seam = new RectangleGeometry(new Rect(2, 11, 24, 1.2));
        var light = new EllipseGeometry(new Point(14, 17.5), 1.1, 1.1);
        return new CombinedGeometry(GeometryCombineMode.Exclude, body,
            new CombinedGeometry(GeometryCombineMode.Union, seam, light));
    }
}
