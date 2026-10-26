using System;
using System.Threading;
using PodPeek.Core;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Storage.Streams;

namespace PodPeek;

/// <summary>
/// Escucha los anuncios Bluetooth de AirPods cercanos y se queda con los nuestros: en una oficina puede haber
/// varios, así que seguimos al que llega con más señal y solo cambiamos si otro llega claramente más fuerte.
/// La dirección Bluetooth de los AirPods cambia cada cierto tiempo (privacidad), por eso no basta con fijarla.
/// </summary>
sealed class PodWatcher : IDisposable
{
    const short MinRssi = -72;          // más lejos de esto no son los tuyos
    const int SwitchMargin = 6;         // dB de ventaja para cambiar a otros AirPods
    static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(8);
    static readonly TimeSpan LostAfter = TimeSpan.FromSeconds(30);

    readonly BluetoothLEAdvertisementWatcher watcher = new() { ScanningMode = BluetoothLEScanningMode.Passive };
    readonly Timer lostTimer;
    readonly object gate = new();

    ulong currentAddress;
    short currentRssi = short.MinValue;
    DateTime lastSeen = DateTime.MinValue;
    bool present;
    DateTime lastRawLog = DateTime.MinValue;

    /// <summary>Nuevo estado (en otro hilo).</summary>
    public event Action<PodStatus>? Updated;
    /// <summary>Hace rato que no se oyen: estuche cerrado o fuera de alcance.</summary>
    public event Action? Lost;

    public PodWatcher()
    {
        watcher.Received += OnReceived;
        watcher.Stopped += (_, e) => Log.Write($"escáner Bluetooth parado: {e.Error}");
        lostTimer = new Timer(_ => CheckLost(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    public void Start()
    {
        try { watcher.Start(); Log.Write("escáner Bluetooth en marcha"); }
        catch (Exception e) { Log.Write($"no se pudo arrancar el escáner Bluetooth: {e.Message}"); }
    }

    void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
    {
        if (args.RawSignalStrengthInDBm < MinRssi) return;
        foreach (var section in args.Advertisement.ManufacturerData)
        {
            if (section.CompanyId != AppleProximity.AppleCompanyId) continue;
            var bytes = new byte[section.Data.Length];
            DataReader.FromBuffer(section.Data).ReadBytes(bytes);
            var status = AppleProximity.Parse(bytes);
            if (status is null) continue;
            Accept(args.BluetoothAddress, args.RawSignalStrengthInDBm, status, bytes);
        }
    }

    void Accept(ulong address, short rssi, PodStatus status, byte[] raw)
    {
        lock (gate)
        {
            var now = DateTime.UtcNow;
            bool same = address == currentAddress;
            bool stale = now - lastSeen > StaleAfter;
            if (!same && !stale && rssi < currentRssi + SwitchMargin) return;

            currentAddress = address;
            currentRssi = rssi;
            lastSeen = now;
            present = true;

            if (now - lastRawLog > TimeSpan.FromMinutes(1))
            {
                lastRawLog = now;
                Log.Write($"anuncio {Convert.ToHexString(raw)} rssi={rssi} -> {status}");
            }
        }
        Updated?.Invoke(status);
    }

    void CheckLost()
    {
        lock (gate)
        {
            if (!present || DateTime.UtcNow - lastSeen < LostAfter) return;
            present = false;
            currentAddress = 0;
            currentRssi = short.MinValue;
        }
        Lost?.Invoke();
    }

    public void Dispose()
    {
        lostTimer.Dispose();
        try { watcher.Stop(); } catch { }
    }
}
