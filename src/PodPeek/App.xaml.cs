using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;
using PodPeek.Core;

namespace PodPeek;

/// <summary>
/// Junta las piezas: el escáner Bluetooth (batería y si los llevas puestos), el audio de Windows (conexión y
/// dispositivo predeterminado), la ventana y el icono de la bandeja.
/// </summary>
public partial class App : Application
{
    Mutex? singleInstance;
    Settings settings = null!;
    AudioManager audio = null!;
    PodWatcher pods = null!;
    PopupWindow popup = null!;
    TrayIcon tray = null!;

    PodStatus? last;
    bool shownSinceSeen, wasBothIn, pausedByUs;
    int? warnedAt;

    protected override void OnStartup(StartupEventArgs e)
    {
        singleInstance = new Mutex(true, "PodPeek.SingleInstance", out bool first);
        if (!first) { Shutdown(); return; }
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) => { Log.Write($"error: {ex.Exception}"); ex.Handled = true; };
        Log.Write($"PodPeek {typeof(App).Assembly.GetName().Version} en Windows {Environment.OSVersion.Version}");

        Theme.Apply(Resources);
        settings = Settings.Load();

        popup = new PopupWindow();
        popup.ConnectRequested += ConnectAsync;

        audio = new AudioManager();
        audio.ConnectionChanged += c => Dispatcher.InvokeAsync(() => OnConnectionChanged(c));

        pods = new PodWatcher();
        pods.Updated += s => Dispatcher.InvokeAsync(() => OnStatus(s));
        pods.Lost += () => Dispatcher.InvokeAsync(OnLost);
        pods.Start();

        tray = new TrayIcon(settings);
        tray.Clicked += () => { Refresh(); popup.Toggle(); };
        tray.ConnectRequested += ConnectAsync;
        tray.ExitRequested += Quit;

        SystemEvents.UserPreferenceChanged += (_, a) =>
        {
            if (a.Category != UserPreferenceCategory.General) return;
            Dispatcher.InvokeAsync(() => { Theme.Apply(Resources); popup.ApplyTheme(); tray.UpdateIcon(); });
        };

        Refresh();
        if (!e.Args.Contains("--background")) popup.ShowFor(TimeSpan.FromSeconds(6));
    }

    void Refresh()
    {
        popup.Update(audio.DeviceName, last, audio.IsConnected);
        tray.Update(last, audio.IsConnected);
    }

    async void OnStatus(PodStatus status)
    {
        last = status;
        Refresh();

        // Al abrir el estuche (empiezan a llegar anuncios), la ventanita, como en el iPhone
        if (!shownSinceSeen && settings.ShowOnConnect)
        {
            shownSinceSeen = true;
            popup.ShowFor(TimeSpan.FromSeconds(6));
        }

        // Batería baja: avisa una vez por bajada; se rearma al cargar
        if (status.LowestPod is int low)
        {
            if (low <= settings.LowBatteryPercent && warnedAt is null)
            {
                warnedAt = low;
                popup.SetMessage($"Batería baja: {low} %");
                popup.ShowFor(TimeSpan.FromSeconds(8));
            }
            else if (low > settings.LowBatteryPercent + 10) warnedAt = null;
        }

        // Pausa al quitarte uno, y sigue al volver a ponértelos (solo si la pausamos nosotros)
        if (settings.PauseOnRemove && audio.IsConnected)
        {
            try
            {
                if (wasBothIn && !status.BothInEar) pausedByUs = await MediaControl.PauseIfPlayingAsync();
                else if (!wasBothIn && status.BothInEar && pausedByUs)
                {
                    pausedByUs = false;
                    await MediaControl.ResumeAsync();
                }
            }
            catch (Exception ex) { Log.Write($"control de medios: {ex.Message}"); }
        }
        wasBothIn = status.BothInEar;
    }

    void OnLost()
    {
        last = null;
        shownSinceSeen = false;
        wasBothIn = false;
        Refresh();
    }

    void OnConnectionChanged(bool connected)
    {
        if (connected && settings.AutoDefaultDevice)
        {
            try { audio.MakeDefault(); }
            catch (Exception ex) { Log.Write($"no se pudieron poner como predeterminados: {ex.Message}"); }
        }
        popup.SetConnecting(false);
        Refresh();
        if (connected && settings.ShowOnConnect) popup.ShowFor(TimeSpan.FromSeconds(5));
    }

    async void ConnectAsync()
    {
        if (audio.IsConnected) { popup.ShowFor(TimeSpan.FromSeconds(4)); return; }
        popup.SetConnecting(true);
        popup.SetMessage(null);
        popup.ShowFor(TimeSpan.FromSeconds(10));
        bool sent = await Task.Run(audio.Connect);
        if (!sent)
        {
            popup.SetConnecting(false);
            popup.SetMessage("No se han podido conectar. Saca los AirPods del estuche y vuelve a probar.");
            return;
        }
        // Si en 10 s no ha llegado la conexión, lo decimos
        await Task.Delay(TimeSpan.FromSeconds(10));
        if (!audio.IsConnected)
        {
            popup.SetConnecting(false);
            popup.SetMessage("Windows no ha conseguido conectarlos. ¿Están cerca y fuera del estuche?");
        }
    }

    void Quit()
    {
        tray.Dispose();
        pods.Dispose();
        audio.Dispose();
        singleInstance?.ReleaseMutex();
        Shutdown();
    }
}
