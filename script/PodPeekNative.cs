// Lo que PowerShell no puede hacer solo: Core Audio (conectar los AirPods y ponerlos como predeterminados) y los
// efectos de ventana de Windows 11. Lo compila Add-Type al arrancar (C# 5, .NET Framework), así que nada de
// sintaxis moderna aquí.
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace PodPeekNative
{
    [StructLayout(LayoutKind.Sequential)]
    struct PropertyKey { public Guid FormatId; public uint PropertyId; }

    [StructLayout(LayoutKind.Sequential)]
    struct PropVariant { public ushort Type; ushort r1, r2, r3; public IntPtr Pointer; IntPtr p2; }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumeratorComObject { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        IMMDeviceCollection EnumAudioEndpoints(int dataFlow, uint stateMask);
        IMMDevice GetDefaultAudioEndpoint(int dataFlow, int role);
        IMMDevice GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection { uint GetCount(); IMMDevice Item(uint index); }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [return: MarshalAs(UnmanagedType.IUnknown)] object Activate(ref Guid iid, uint clsCtx, IntPtr p);
        IPropertyStore OpenPropertyStore(uint access);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetId();
        uint GetState();
    }

    [ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore { uint GetCount(); PropertyKey GetAt(uint i); void GetValue(ref PropertyKey key, out PropVariant v); }

    [ComImport, Guid("2A07407E-6497-4A18-9787-32F79BD0D98F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IDeviceTopology
    {
        uint GetConnectorCount();
        IConnector GetConnector(uint index);
        uint GetSubunitCount();
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetSubunit(uint index);
        IPart GetPartById(uint id);
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetDeviceId();
    }

    [ComImport, Guid("9C2C4058-23F5-41DE-877A-DF3AF236A09E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IConnector
    {
        int GetConnectorType(); int GetDataFlow(); void ConnectTo(IConnector c); void Disconnect();
        [return: MarshalAs(UnmanagedType.Bool)] bool IsConnected();
        IConnector GetConnectedTo();
    }

    [ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPart
    {
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetName();
        uint GetLocalId();
        [return: MarshalAs(UnmanagedType.LPWStr)] string GetGlobalId();
        int GetPartType(); Guid GetSubType(); uint GetControlInterfaceCount();
        [return: MarshalAs(UnmanagedType.IUnknown)] object GetControlInterface(uint i);
        [return: MarshalAs(UnmanagedType.IUnknown)] object EnumPartsIncoming();
        [return: MarshalAs(UnmanagedType.IUnknown)] object EnumPartsOutgoing();
        IDeviceTopology GetTopologyObject();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct KsProperty { public Guid Set; public uint Id; public uint Flags; }

    [ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IKsControl
    {
        [PreserveSig] int KsProperty(ref KsProperty p, uint pLen, IntPtr data, uint dataLen, out uint returned);
    }

    [ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
    class PolicyConfigComObject { }

    [ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        void U1(); void U2(); void U3(); void U4(); void U5(); void U6(); void U7(); void U8(); void U9(); void U10();
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }

    public static class Audio
    {
        const int Render = 0, Capture = 1;
        const uint Active = 0x1, AllStates = 0xF;
        static readonly Guid BtAudio = new Guid("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");

        [DllImport("ole32.dll")] static extern int PropVariantClear(ref PropVariant v);

        class Endpoint { public IMMDevice Device; public string Id; public string Name; }

        static IMMDeviceEnumerator Enumerator() { return (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject(); }

        static string FriendlyName(IMMDevice device)
        {
            try
            {
                var key = new PropertyKey { FormatId = new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), PropertyId = 14 };
                PropVariant v;
                device.OpenPropertyStore(0).GetValue(ref key, out v);
                try { return v.Type == 31 ? Marshal.PtrToStringUni(v.Pointer) : null; }
                finally { PropVariantClear(ref v); }
            }
            catch (COMException) { return null; }
        }

        static List<Endpoint> FindAirPods(int flow, uint mask)
        {
            var found = new List<Endpoint>();
            try
            {
                var all = Enumerator().EnumAudioEndpoints(flow, mask);
                uint n = all.GetCount();
                for (uint i = 0; i < n; i++)
                {
                    var d = all.Item(i);
                    string name = FriendlyName(d);
                    if (name != null && name.IndexOf("AirPods", StringComparison.OrdinalIgnoreCase) >= 0)
                        found.Add(new Endpoint { Device = d, Id = d.GetId(), Name = name });
                }
            }
            catch (COMException) { }
            return found;
        }

        static bool IsHandsFree(string name)
        {
            return name.IndexOf("manos libres", StringComparison.OrdinalIgnoreCase) >= 0 ||
                   name.IndexOf("hands-free", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsConnected() { return FindAirPods(Render, Active).Count > 0; }

        /// <summary>"Auriculares (AirPods Pro de Diego)" -> "AirPods Pro de Diego"; null si no están emparejados.</summary>
        public static string DeviceName()
        {
            var list = FindAirPods(Render, AllStates);
            if (list.Count == 0) return null;
            string name = list[0].Name;
            int open = name.IndexOf('('), close = name.LastIndexOf(')');
            return open >= 0 && close > open ? name.Substring(open + 1, close - open - 1) : name;
        }

        /// <summary>Salida y micrófono predeterminados para todo, también para llamadas (Teams).</summary>
        public static string MakeDefault()
        {
            var policy = (IPolicyConfig)new PolicyConfigComObject();
            Endpoint output = null, input = null;
            foreach (var e in FindAirPods(Render, Active))
                if (output == null || (IsHandsFree(output.Name) && !IsHandsFree(e.Name))) output = e;
            foreach (var e in FindAirPods(Capture, Active)) { input = e; break; }
            for (int role = 0; role <= 2; role++)
            {
                if (output != null) policy.SetDefaultEndpoint(output.Id, role);
                if (input != null) policy.SetDefaultEndpoint(input.Id, role);
            }
            return "salida=" + (output != null ? output.Name : "-") + " entrada=" + (input != null ? input.Name : "-");
        }

        /// <summary>Lo mismo que el botón "Conectar" de Configuración: KSPROPERTY_ONESHOT_RECONNECT al driver.</summary>
        public static string Connect() { return OneShot(0); }
        public static string Disconnect() { return OneShot(1); }

        static string OneShot(uint id)
        {
            var log = new List<string>();
            foreach (var e in FindAirPods(Render, AllStates))
            {
                try
                {
                    var topoIid = typeof(IDeviceTopology).GUID;
                    var topology = (IDeviceTopology)e.Device.Activate(ref topoIid, 0x17, IntPtr.Zero);
                    var connector = topology.GetConnector(0).GetConnectedTo();
                    string filterId = ((IPart)connector).GetTopologyObject().GetDeviceId();
                    var ksIid = typeof(IKsControl).GUID;
                    var ks = (IKsControl)Enumerator().GetDevice(filterId).Activate(ref ksIid, 0x17, IntPtr.Zero);
                    var p = new KsProperty { Set = BtAudio, Id = id, Flags = 1 };
                    uint returned;
                    int hr = ks.KsProperty(ref p, (uint)Marshal.SizeOf(typeof(KsProperty)), IntPtr.Zero, 0, out returned);
                    log.Add(e.Name + ": 0x" + hr.ToString("X8"));
                }
                catch (Exception ex) { log.Add(e.Name + ": " + ex.Message); }
            }
            return log.Count == 0 ? "no hay AirPods emparejados" : string.Join("; ", log.ToArray());
        }
    }

    /// <summary>Acrílico, esquinas redondeadas y modo oscuro de Windows 11; ventana que no roba el foco.</summary>
    public static class Win
    {
        [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr h, int attr, ref int v, int size);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] static extern IntPtr GetLong(IntPtr h, int i);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")] static extern IntPtr SetLong(IntPtr h, int i, IntPtr v);

        public static int Build { get { return Environment.OSVersion.Version.Build; } }
        public static bool SupportsBackdrop { get { return Build >= 22621; } }

        public static void Apply(IntPtr hwnd, bool dark)
        {
            int v = dark ? 1 : 0;
            DwmSetWindowAttribute(hwnd, 20, ref v, 4);           // modo oscuro
            if (Build >= 22000) { v = 2; DwmSetWindowAttribute(hwnd, 33, ref v, 4); }   // esquinas redondeadas
            if (SupportsBackdrop) { v = 3; DwmSetWindowAttribute(hwnd, 38, ref v, 4); } // acrílico
        }

        public static void MakeToolWindow(IntPtr hwnd)
        {
            long style = GetLong(hwnd, -20).ToInt64();
            SetLong(hwnd, -20, new IntPtr(style | 0x80 | 0x08000000)); // WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE
        }

        static int ThemeFlag(string name)
        {
            using (var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
            {
                object v = key == null ? null : key.GetValue(name);
                return v is int ? (int)v : 1;
            }
        }

        public static bool AppsDark { get { return ThemeFlag("AppsUseLightTheme") == 0; } }
        public static bool TaskbarDark { get { return ThemeFlag("SystemUsesLightTheme") == 0; } }
    }
}

namespace PodPeekNative
{
    using System.Reflection;
    using System.Runtime.InteropServices.WindowsRuntime;

    /// <summary>Un anuncio Bluetooth de unos AirPods, ya extraído a tipos normales de .NET.</summary>
    public sealed class AdvSample
    {
        public ulong Address;
        public short Rssi;
        public byte[] Data;
    }

    /// <summary>
    /// El escáner Bluetooth para PowerShell 5.1, que ni se suscribe a eventos WinRT (Register-ObjectEvent los rechaza)
    /// ni reconoce sus listas. Todo por reflexión, para no tener que compilar contra las librerías WinRT:
    /// la suscripción con WindowsRuntimeMarshal.AddEventHandler (como hace .NET por dentro) y los datos de cada
    /// anuncio se copian a AdvSample. PowerShell recoge la cola cada poco con Drain().
    /// </summary>
    public static class BleScanner
    {
        const ushort Apple = 0x004C;
        static readonly object Gate = new object();
        static readonly List<AdvSample> Queue = new List<AdvSample>();
        static Type dataReaderType;
        public static string LastError = "";

        static Type WinRT(string name, string assembly)
        {
            return Type.GetType(name + ", " + assembly + ", ContentType=WindowsRuntime", true);
        }

        /// <summary>Crea el escáner (solo anuncios de Apple, con señal suficiente), se suscribe y lo arranca.</summary>
        public static object Start(short minRssi)
        {
            Type watcherType = WinRT("Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher", "Windows.Devices.Bluetooth");
            Type mdType = WinRT("Windows.Devices.Bluetooth.Advertisement.BluetoothLEManufacturerData", "Windows.Devices.Bluetooth");
            Type scanningMode = WinRT("Windows.Devices.Bluetooth.Advertisement.BluetoothLEScanningMode", "Windows.Devices.Bluetooth");
            dataReaderType = WinRT("Windows.Storage.Streams.DataReader", "Windows.Storage.Streams");

            object watcher = Activator.CreateInstance(watcherType);
            watcherType.GetProperty("ScanningMode").SetValue(watcher, Enum.Parse(scanningMode, "Passive"), null);
            object rssiFilter = watcherType.GetProperty("SignalStrengthFilter").GetValue(watcher, null);
            rssiFilter.GetType().GetProperty("InRangeThresholdInDBm").SetValue(rssiFilter, (short?)minRssi, null);
            try
            {
                object md = Activator.CreateInstance(mdType);
                mdType.GetProperty("CompanyId").SetValue(md, Apple, null);
                object filter = watcherType.GetProperty("AdvertisementFilter").GetValue(watcher, null);
                object adv = filter.GetType().GetProperty("Advertisement").GetValue(filter, null);
                object list = adv.GetType().GetProperty("ManufacturerData").GetValue(adv, null);
                typeof(ICollection<>).MakeGenericType(mdType).GetMethod("Add").Invoke(list, new object[] { md });
            }
            catch (Exception e) { LastError = "filtro de Apple no aplicado: " + Inner(e).Message; }

            Subscribe(watcher, "Received", typeof(BleScanner).GetMethod("OnReceived"));
            watcherType.GetMethod("Start").Invoke(watcher, null);
            return watcher;
        }

        public static string Status(object watcher)
        {
            return watcher.GetType().GetProperty("Status").GetValue(watcher, null).ToString();
        }

        public static void Stop(object watcher)
        {
            try { watcher.GetType().GetMethod("Stop").Invoke(watcher, null); } catch { }
        }

        static void Subscribe(object target, string eventName, MethodInfo staticHandler)
        {
            EventInfo ev = target.GetType().GetEvent(eventName);
            Type handlerType = ev.EventHandlerType;
            Type tokenType = typeof(EventRegistrationToken);
            Delegate handler = Delegate.CreateDelegate(handlerType, staticHandler);
            Delegate add = Delegate.CreateDelegate(typeof(Func<,>).MakeGenericType(handlerType, tokenType), target, ev.GetAddMethod());
            Delegate remove = Delegate.CreateDelegate(typeof(Action<>).MakeGenericType(tokenType), target, ev.GetRemoveMethod());
            typeof(WindowsRuntimeMarshal).GetMethod("AddEventHandler").MakeGenericMethod(handlerType)
                .Invoke(null, new object[] { add, remove, handler });
        }

        public static void OnReceived(object sender, object args)
        {
            try
            {
                Type t = args.GetType();
                short rssi = (short)t.GetProperty("RawSignalStrengthInDBm").GetValue(args, null);
                ulong address = (ulong)t.GetProperty("BluetoothAddress").GetValue(args, null);
                object adv = t.GetProperty("Advertisement").GetValue(args, null);
                PropertyInfo mdProp = adv.GetType().GetProperty("ManufacturerData");
                object list = mdProp.GetValue(adv, null);
                Type itemType = mdProp.PropertyType.GetGenericArguments()[0];
                var items = (System.Collections.IEnumerator)typeof(IEnumerable<>).MakeGenericType(itemType)
                    .GetMethod("GetEnumerator").Invoke(list, null);
                while (items.MoveNext())
                {
                    object md = items.Current;
                    if ((ushort)itemType.GetProperty("CompanyId").GetValue(md, null) != Apple) continue;
                    byte[] data = ReadBuffer(itemType.GetProperty("Data").GetValue(md, null));
                    if (data.Length < 9 || data[0] != 0x07) continue; // solo el mensaje de los AirPods
                    lock (Gate)
                    {
                        if (Queue.Count >= 200) Queue.RemoveAt(0);
                        Queue.Add(new AdvSample { Address = address, Rssi = rssi, Data = data });
                    }
                }
            }
            catch (Exception e) { LastError = "anuncio ilegible: " + Inner(e).Message; }
        }

        static byte[] ReadBuffer(object buffer)
        {
            object reader = dataReaderType.GetMethod("FromBuffer").Invoke(null, new object[] { buffer });
            uint length = (uint)dataReaderType.GetProperty("UnconsumedBufferLength").GetValue(reader, null);
            byte[] data = new byte[length];
            dataReaderType.GetMethod("ReadBytes").Invoke(reader, new object[] { data });
            return data;
        }

        static Exception Inner(Exception e) { return e.InnerException ?? e; }

        public static AdvSample[] Drain()
        {
            lock (Gate)
            {
                AdvSample[] all = Queue.ToArray();
                Queue.Clear();
                return all;
            }
        }
    }
}
