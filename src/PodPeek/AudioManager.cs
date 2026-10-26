using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using PodPeek.Native;

namespace PodPeek;

/// <summary>
/// Los AirPods como dispositivo de audio de Windows: saber si están conectados, conectarlos con un clic y
/// ponerlos como predeterminados (también para llamadas, que es lo que usa Teams) nada más conectarse.
/// </summary>
[ComVisible(true)]
sealed class AudioManager : IMMNotificationClient, IDisposable
{
    readonly IMMDeviceEnumerator enumerator = (IMMDeviceEnumerator)new MMDeviceEnumeratorComObject();
    bool connected;

    /// <summary>Se dispara (en otro hilo) cuando los AirPods pasan de desconectados a conectados o al revés.</summary>
    public event Action<bool>? ConnectionChanged;

    public AudioManager()
    {
        connected = FindAirPods(EDataFlow.Render, DeviceState.Active).Any();
        enumerator.RegisterEndpointNotificationCallback(this);
    }

    public bool IsConnected => connected;

    /// <summary>Nombre que Windows da a los AirPods (p. ej. "AirPods Pro de Diego"), si están emparejados.</summary>
    public string? DeviceName
    {
        get
        {
            var name = FindAirPods(EDataFlow.Render, DeviceState.All).Select(d => d.Name).FirstOrDefault();
            if (name is null) return null;
            // "Auriculares (AirPods Pro de Diego)" -> "AirPods Pro de Diego"
            int open = name.IndexOf('('), close = name.LastIndexOf(')');
            return open >= 0 && close > open ? name[(open + 1)..close] : name;
        }
    }

    record Endpoint(IMMDevice Device, string Id, string Name);

    IEnumerable<Endpoint> FindAirPods(EDataFlow flow, uint stateMask)
    {
        IMMDeviceCollection collection;
        try { collection = enumerator.EnumAudioEndpoints(flow, stateMask); }
        catch (COMException) { yield break; }
        uint count = collection.GetCount();
        for (uint i = 0; i < count; i++)
        {
            var device = collection.Item(i);
            var name = CoreAudio.GetFriendlyName(device);
            if (name is null || !name.Contains("AirPods", StringComparison.OrdinalIgnoreCase)) continue;
            yield return new Endpoint(device, device.GetId(), name);
        }
    }

    /// <summary>Pone los AirPods como salida y entrada predeterminadas para todo (música y llamadas).</summary>
    public void MakeDefault()
    {
        var policy = (IPolicyConfig)new PolicyConfigComObject();
        // De salida, mejor el perfil estéreo que el de manos libres si Windows muestra los dos
        var output = FindAirPods(EDataFlow.Render, DeviceState.Active)
            .OrderBy(e => IsHandsFree(e.Name) ? 1 : 0).FirstOrDefault();
        var input = FindAirPods(EDataFlow.Capture, DeviceState.Active).FirstOrDefault();
        foreach (var role in new[] { ERole.Console, ERole.Multimedia, ERole.Communications })
        {
            if (output is not null) policy.SetDefaultEndpoint(output.Id, role);
            if (input is not null) policy.SetDefaultEndpoint(input.Id, role);
        }
        Log.Write($"predeterminados: salida={output?.Name ?? "-"} entrada={input?.Name ?? "-"}");
    }

    static bool IsHandsFree(string name) =>
        name.Contains("manos libres", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("hands-free", StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Pide al driver Bluetooth que conecte los AirPods (KSPROPERTY_ONESHOT_RECONNECT), como el botón
    /// "Conectar" de Configuración. Devuelve false si no están emparejados o el driver lo rechaza.
    /// </summary>
    public bool Connect() => SendOneShot(CoreAudio.KsPropertyOneShotReconnect);

    public bool Disconnect() => SendOneShot(CoreAudio.KsPropertyOneShotDisconnect);

    bool SendOneShot(uint propertyId)
    {
        bool sent = false;
        foreach (var endpoint in FindAirPods(EDataFlow.Render, DeviceState.All))
        {
            try
            {
                var iid = CoreAudio.IidDeviceTopology;
                var topology = (IDeviceTopology)endpoint.Device.Activate(ref iid, CoreAudio.ClsctxAll, IntPtr.Zero);
                var connector = topology.GetConnector(0).GetConnectedTo();
                var filterId = ((IPart)connector).GetTopologyObject().GetDeviceId();
                var filter = enumerator.GetDevice(filterId);
                var ksIid = CoreAudio.IidKsControl;
                var ks = (IKsControl)filter.Activate(ref ksIid, CoreAudio.ClsctxAll, IntPtr.Zero);
                var property = new KsProperty { Set = CoreAudio.KsPropSetBtAudio, Id = propertyId, Flags = CoreAudio.KsPropertyTypeGet };
                int hr = ks.KsProperty(ref property, (uint)Marshal.SizeOf<KsProperty>(), IntPtr.Zero, 0, out _);
                Log.Write($"one-shot {propertyId} a {endpoint.Name}: 0x{hr:X8}");
                sent |= hr >= 0;
            }
            catch (Exception e) when (e is COMException or InvalidCastException)
            {
                Log.Write($"one-shot {propertyId} a {endpoint.Name} falló: {e.Message}");
            }
        }
        return sent;
    }

    void Refresh()
    {
        bool now = FindAirPods(EDataFlow.Render, DeviceState.Active).Any();
        if (now == connected) return;
        connected = now;
        Log.Write(now ? "AirPods conectados" : "AirPods desconectados");
        ConnectionChanged?.Invoke(now);
    }

    void IMMNotificationClient.OnDeviceStateChanged(string deviceId, uint newState) => Refresh();
    void IMMNotificationClient.OnDeviceAdded(string deviceId) => Refresh();
    void IMMNotificationClient.OnDeviceRemoved(string deviceId) => Refresh();
    void IMMNotificationClient.OnDefaultDeviceChanged(EDataFlow flow, ERole role, string? defaultDeviceId) { }
    void IMMNotificationClient.OnPropertyValueChanged(string deviceId, PropertyKey key) { }

    public void Dispose()
    {
        try { enumerator.UnregisterEndpointNotificationCallback(this); } catch (COMException) { }
    }
}
