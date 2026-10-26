using System;
using System.Runtime.InteropServices;

// Interop mínimo con Core Audio (el sistema de audio de Windows). Solo lo que usa PodPeek:
// listar dispositivos, enterarse de cuándo se conectan, ponerlos como predeterminados y pedir al driver
// Bluetooth que los conecte (lo mismo que el botón "Conectar" de Configuración).
namespace PodPeek.Native;

enum EDataFlow { Render = 0, Capture = 1, All = 2 }
enum ERole { Console = 0, Multimedia = 1, Communications = 2 }

static class DeviceState
{
    public const uint Active = 0x1;
    public const uint Disabled = 0x2;
    public const uint NotPresent = 0x4;
    public const uint Unplugged = 0x8;
    public const uint All = 0xF;
}

[StructLayout(LayoutKind.Sequential)]
struct PropertyKey
{
    public Guid FormatId;
    public uint PropertyId;
    public PropertyKey(Guid formatId, uint propertyId) { FormatId = formatId; PropertyId = propertyId; }
}

[StructLayout(LayoutKind.Sequential)]
struct PropVariant
{
    public ushort Type;
    ushort reserved1, reserved2, reserved3;
    public IntPtr Pointer;
    IntPtr pointer2;
}

[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
class MMDeviceEnumeratorComObject { }

[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceEnumerator
{
    IMMDeviceCollection EnumAudioEndpoints(EDataFlow dataFlow, uint stateMask);
    IMMDevice GetDefaultAudioEndpoint(EDataFlow dataFlow, ERole role);
    IMMDevice GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id);
    void RegisterEndpointNotificationCallback(IMMNotificationClient client);
    void UnregisterEndpointNotificationCallback(IMMNotificationClient client);
}

[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDeviceCollection
{
    uint GetCount();
    IMMDevice Item(uint index);
}

[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMDevice
{
    [return: MarshalAs(UnmanagedType.IUnknown)]
    object Activate(ref Guid iid, uint clsCtx, IntPtr activationParams);
    IPropertyStore OpenPropertyStore(uint access);
    [return: MarshalAs(UnmanagedType.LPWStr)]
    string GetId();
    uint GetState();
}

[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPropertyStore
{
    uint GetCount();
    PropertyKey GetAt(uint index);
    void GetValue(ref PropertyKey key, out PropVariant value);
}

[ComImport, Guid("7991EEC9-7E89-4D85-8390-6C703CEC60C0"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IMMNotificationClient
{
    void OnDeviceStateChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, uint newState);
    void OnDeviceAdded([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDeviceRemoved([MarshalAs(UnmanagedType.LPWStr)] string deviceId);
    void OnDefaultDeviceChanged(EDataFlow flow, ERole role, [MarshalAs(UnmanagedType.LPWStr)] string? defaultDeviceId);
    void OnPropertyValueChanged([MarshalAs(UnmanagedType.LPWStr)] string deviceId, PropertyKey key);
}

// --- Topología: para llegar del dispositivo de audio al driver Bluetooth que hay detrás ---

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
    int GetConnectorType();
    int GetDataFlow();
    void ConnectTo(IConnector connectTo);
    void Disconnect();
    [return: MarshalAs(UnmanagedType.Bool)] bool IsConnected();
    IConnector GetConnectedTo();
}

[ComImport, Guid("AE2DE0E4-5BCA-4F2D-AA46-5D13F8FDB3A9"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPart
{
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetName();
    uint GetLocalId();
    [return: MarshalAs(UnmanagedType.LPWStr)] string GetGlobalId();
    int GetPartType();
    Guid GetSubType();
    uint GetControlInterfaceCount();
    [return: MarshalAs(UnmanagedType.IUnknown)] object GetControlInterface(uint index);
    [return: MarshalAs(UnmanagedType.IUnknown)] object EnumPartsIncoming();
    [return: MarshalAs(UnmanagedType.IUnknown)] object EnumPartsOutgoing();
    IDeviceTopology GetTopologyObject();
}

[StructLayout(LayoutKind.Sequential)]
struct KsProperty
{
    public Guid Set;
    public uint Id;
    public uint Flags;
}

[ComImport, Guid("28F54685-06FD-11D2-B27A-00A0C9223196"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IKsControl
{
    [PreserveSig]
    int KsProperty(ref KsProperty property, uint propertyLength, IntPtr data, uint dataLength, out uint bytesReturned);
}

// --- Poner un dispositivo como predeterminado (interfaz no documentada; la usan SoundSwitch, EarTrumpet…) ---

[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")]
class PolicyConfigComObject { }

[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IPolicyConfig
{
    void Unused1(); void Unused2(); void Unused3(); void Unused4(); void Unused5();
    void Unused6(); void Unused7(); void Unused8(); void Unused9(); void Unused10();
    [PreserveSig]
    int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
}

static class CoreAudio
{
    public const uint ClsctxAll = 0x17;
    public static readonly PropertyKey FriendlyName = new(new Guid("A45C254E-DF1C-4EFD-8020-67D146A850E0"), 14);
    public static Guid IidDeviceTopology = typeof(IDeviceTopology).GUID;
    public static Guid IidKsControl = typeof(IKsControl).GUID;

    // KSPROPSETID_BtAudio (ksmedia.h)
    public static readonly Guid KsPropSetBtAudio = new("7FA06C40-B8F6-4C7E-8556-E8C33A12E54D");
    public const uint KsPropertyOneShotReconnect = 0;
    public const uint KsPropertyOneShotDisconnect = 1;
    public const uint KsPropertyTypeGet = 0x1;

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PropVariant value);

    public static string? GetFriendlyName(IMMDevice device)
    {
        try
        {
            var store = device.OpenPropertyStore(0);
            var key = FriendlyName;
            store.GetValue(ref key, out var value);
            try { return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null; }
            finally { PropVariantClear(ref value); }
        }
        catch (COMException) { return null; }
    }
}
