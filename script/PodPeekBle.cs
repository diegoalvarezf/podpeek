// Escáner Bluetooth para la versión script. PowerShell 5.1 no puede suscribirse a eventos WinRT, así que el
// evento lo recibe esta clase y PowerShell recoge los anuncios cada poco con Drain(). Lo compila el csc de
// .NET Framework (C# 5), que no entiende "+=" con eventos WinRT: por eso se llama a add_Received directamente.
using System;
using System.Collections.Generic;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Foundation;
using Windows.Storage.Streams;

namespace PodPeekNative
{
    public sealed class AdvSample
    {
        public ulong Address;
        public short Rssi;
        public byte[] Data;
    }

    public static class Ble
    {
        static BluetoothLEAdvertisementWatcher watcher;
        static readonly object Gate = new object();
        static readonly List<AdvSample> Queue = new List<AdvSample>();
        static string lastError = "";

        public static string Start(short minRssi)
        {
            watcher = new BluetoothLEAdvertisementWatcher();
            watcher.ScanningMode = BluetoothLEScanningMode.Passive;
            var apple = new BluetoothLEManufacturerData();
            apple.CompanyId = 0x004C;
            watcher.AdvertisementFilter.Advertisement.ManufacturerData.Add(apple);
            watcher.SignalStrengthFilter.InRangeThresholdInDBm = minRssi;
            watcher.add_Received(new TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementReceivedEventArgs>(OnReceived));
            watcher.add_Stopped(new TypedEventHandler<BluetoothLEAdvertisementWatcher, BluetoothLEAdvertisementWatcherStoppedEventArgs>(
                (s, e) => { lastError = e.Error.ToString(); }));
            watcher.Start();
            return watcher.Status.ToString();
        }

        static void OnReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            foreach (var section in args.Advertisement.ManufacturerData)
            {
                if (section.CompanyId != 0x004C) continue;
                byte[] data = new byte[section.Data.Length];
                DataReader.FromBuffer(section.Data).ReadBytes(data);
                if (data.Length < 9 || data[0] != 0x07) continue; // solo el mensaje de los AirPods
                lock (Gate)
                {
                    if (Queue.Count >= 200) Queue.RemoveAt(0);
                    Queue.Add(new AdvSample { Address = args.BluetoothAddress, Rssi = args.RawSignalStrengthInDBm, Data = data });
                }
            }
        }

        public static AdvSample[] Drain()
        {
            lock (Gate)
            {
                var all = Queue.ToArray();
                Queue.Clear();
                return all;
            }
        }

        public static string Status
        {
            get { return watcher == null ? "sin arrancar" : watcher.Status + (lastError.Length > 0 ? " (" + lastError + ")" : ""); }
        }

        public static void Stop()
        {
            if (watcher != null) { try { watcher.Stop(); } catch { } }
        }
    }
}
