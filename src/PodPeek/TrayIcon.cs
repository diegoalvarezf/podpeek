using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using PodPeek.Core;
using PodPeek.Native;

namespace PodPeek;

/// <summary>Icono junto al reloj: clic para ver la batería, clic derecho para las opciones.</summary>
sealed class TrayIcon : IDisposable
{
    readonly NotifyIcon icon = new() { Text = "PodPeek", Visible = true };
    readonly Settings settings;
    Icon? current;

    public event Action? Clicked;
    public event Action? ConnectRequested;
    public event Action? ExitRequested;

    public TrayIcon(Settings settings)
    {
        this.settings = settings;
        icon.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) Clicked?.Invoke(); };
        icon.ContextMenuStrip = BuildMenu();
        UpdateIcon();
    }

    ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip { ShowImageMargin = false, Font = new Font("Segoe UI", 9.5f) };
        menu.Items.Add("Mostrar batería", null, (_, _) => Clicked?.Invoke());
        menu.Items.Add("Conectar AirPods", null, (_, _) => ConnectRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(Toggle("Pausar la música al quitármelos", () => settings.PauseOnRemove, v => settings.PauseOnRemove = v));
        menu.Items.Add(Toggle("Usarlos para todo al conectarlos (también Teams)", () => settings.AutoDefaultDevice, v => settings.AutoDefaultDevice = v));
        menu.Items.Add(Toggle("Mostrar la batería al conectarlos", () => settings.ShowOnConnect, v => settings.ShowOnConnect = v));
        menu.Items.Add(Toggle("Iniciar con Windows", () => Settings.StartWithWindows, v => Settings.StartWithWindows = v, save: false));
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Abrir registro", null, (_, _) => Process.Start(new ProcessStartInfo(Log.Dir) { UseShellExecute = true }));
        menu.Items.Add("Salir", null, (_, _) => ExitRequested?.Invoke());
        return menu;
    }

    ToolStripMenuItem Toggle(string text, Func<bool> get, Action<bool> set, bool save = true)
    {
        var item = new ToolStripMenuItem(text) { CheckOnClick = true, Checked = get() };
        item.CheckedChanged += (_, _) =>
        {
            set(item.Checked);
            if (save) settings.Save();
        };
        return item;
    }

    public void Update(PodStatus? status, bool connected)
    {
        string text = status is null
            ? connected ? "AirPods conectados" : "PodPeek · AirPods no detectados"
            : $"I {Fmt(status.Left)} · D {Fmt(status.Right)} · Estuche {Fmt(status.Case)}";
        icon.Text = text.Length > 63 ? text[..63] : text;
    }

    static string Fmt(int? v) => v is int p ? $"{p} %" : "—";

    /// <summary>Dibuja los dos auriculares en blanco o negro según el color de la barra de tareas.</summary>
    public void UpdateIcon()
    {
        int size = 32;
        using var bmp = new Bitmap(size, size);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using var brush = new SolidBrush(Dwm.TaskbarIsDark ? Color.White : Color.FromArgb(0x1D, 0x1D, 0x1F));
            DrawPod(g, brush, 2, mirror: false);
            DrawPod(g, brush, 17, mirror: true);
        }
        var old = current;
        IntPtr handle = bmp.GetHicon();
        current = (Icon)Icon.FromHandle(handle).Clone();
        DestroyIcon(handle);
        icon.Icon = current;
        old?.Dispose();
    }

    static void DrawPod(Graphics g, Brush brush, float x, bool mirror)
    {
        // cabeza redonda + varilla, como el icono de AirPods
        float headX = mirror ? x + 3 : x;
        g.FillEllipse(brush, headX, 3, 11, 11);
        float stemX = mirror ? x + 3 : x + 6;
        using var stem = Rounded(stemX, 8, 5, 20, 2.5f);
        g.FillPath(brush, stem);
    }

    static GraphicsPath Rounded(float x, float y, float w, float h, float r)
    {
        var path = new GraphicsPath();
        path.AddArc(x, y, r * 2, r * 2, 180, 90);
        path.AddArc(x + w - r * 2, y, r * 2, r * 2, 270, 90);
        path.AddArc(x + w - r * 2, y + h - r * 2, r * 2, r * 2, 0, 90);
        path.AddArc(x, y + h - r * 2, r * 2, r * 2, 90, 90);
        path.CloseFigure();
        return path;
    }

    [DllImport("user32.dll")]
    static extern bool DestroyIcon(IntPtr handle);

    public void Dispose()
    {
        icon.Visible = false;
        icon.Dispose();
        current?.Dispose();
    }
}
