using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using PodPeek.Core;
using PodPeek.Native;

namespace PodPeek;

/// <summary>La ventanita que aparece abajo a la derecha, como la del iPhone al abrir el estuche.</summary>
public partial class PopupWindow : Window
{
    const double Margin_ = 12;
    readonly BatteryRing left = new("Izquierdo", BatteryRing.LeftPod());
    readonly BatteryRing right = new("Derecho", BatteryRing.RightPod());
    readonly BatteryRing @case = new("Estuche", BatteryRing.Case());
    readonly DispatcherTimer hideTimer = new();
    bool showing;

    public event Action? ConnectRequested;

    public PopupWindow()
    {
        InitializeComponent();
        Rings.Children.Add(left);
        Rings.Children.Add(right);
        Rings.Children.Add(@case);
        hideTimer.Tick += (_, _) => HideAnimated();
        MouseEnter += (_, _) => hideTimer.Stop();
        MouseLeave += (_, _) => { if (showing) hideTimer.Start(); };
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            // Fondo transparente para que se vea el acrílico de Windows 11 detrás
            if (HwndSource.FromHwnd(hwnd)?.CompositionTarget is { } target) target.BackgroundColor = Colors.Transparent;
            Dwm.MakeToolWindow(hwnd);
            ApplyTheme();
        };
    }

    public void ApplyTheme()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) Dwm.Apply(hwnd, Theme.Dark);
    }

    public void Update(string? name, PodStatus? status, bool connected)
    {
        TitleText.Text = name ?? (status is null ? "AirPods" : AppleProximity.ModelName(status.Model));
        StatusText.Text = connected ? "Conectados" : status is null ? "Sin conectar" : "Cerca, sin conectar";
        StatusDot.Fill = new SolidColorBrush(connected ? Theme.Green : Color.FromRgb(0x8E, 0x8E, 0x93));
        ConnectButton.Visibility = connected ? Visibility.Collapsed : Visibility.Visible;
        if (connected) SetMessage(null);

        bool animate = IsVisible;
        left.SetLevel(status?.Left, status?.LeftCharging ?? false, animate);
        right.SetLevel(status?.Right, status?.RightCharging ?? false, animate);
        @case.SetLevel(status?.Case, status?.CaseCharging ?? false, animate);
    }

    public void SetMessage(string? text)
    {
        MessageText.Text = text ?? "";
        MessageText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    public void SetConnecting(bool busy)
    {
        ConnectButton.IsEnabled = !busy;
        ConnectButton.Content = busy ? "Conectando…" : "Conectar";
    }

    /// <summary>Aparece deslizándose desde abajo y se oculta sola pasado un rato (salvo con el ratón encima).</summary>
    public void ShowFor(TimeSpan duration)
    {
        hideTimer.Interval = duration;
        hideTimer.Stop();
        if (!showing)
        {
            showing = true;
            Root.Opacity = 0;
            Show();
            UpdateLayout();
            Place();
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
            Root.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220)) { EasingFunction = ease });
            Slide.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(18, 0, TimeSpan.FromMilliseconds(320)) { EasingFunction = ease });
            left.Replay(); right.Replay(); @case.Replay();
        }
        if (!IsMouseOver) hideTimer.Start();
    }

    public void Toggle()
    {
        if (showing) HideAnimated();
        else ShowFor(TimeSpan.FromSeconds(8));
    }

    public void HideAnimated()
    {
        hideTimer.Stop();
        if (!showing) return;
        showing = false;
        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn } };
        fade.Completed += (_, _) => { if (!showing) Hide(); };
        Root.BeginAnimation(OpacityProperty, fade);
    }

    void Place()
    {
        var area = SystemParameters.WorkArea;
        Left = area.Right - ActualWidth - Margin_;
        Top = area.Bottom - ActualHeight - Margin_;
    }

    void OnConnectClick(object sender, RoutedEventArgs e) => ConnectRequested?.Invoke();

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        hideTimer.Stop();
    }
}
