using System;

namespace PodPeek.Core;

/// <summary>Estado de unos AirPods leído de su anuncio Bluetooth.</summary>
/// <param name="Model">Modelo (bytes 3-4 del anuncio, p. ej. 0x0E20 = AirPods Pro).</param>
/// <param name="Left">Batería del izquierdo en %, o null si no se sabe (fuera del estuche y lejos, etc.).</param>
public sealed record PodStatus(
    ushort Model,
    int? Left,
    int? Right,
    int? Case,
    bool LeftCharging,
    bool RightCharging,
    bool CaseCharging,
    bool LeftInEar,
    bool RightInEar)
{
    /// <summary>Intensidad de la señal en dBm (la rellena quien recibe el anuncio).</summary>
    public short Rssi { get; init; } = short.MinValue;

    public bool BothInEar => LeftInEar && RightInEar;
    public bool AnyInEar => LeftInEar || RightInEar;

    /// <summary>La batería más baja de los dos auriculares (la que importa para avisar).</summary>
    public int? LowestPod => (Left, Right) switch
    {
        (int l, int r) => Math.Min(l, r),
        (int l, null) => l,
        (null, int r) => r,
        _ => null,
    };
}

/// <summary>
/// Lee el mensaje "proximity pairing" que los AirPods emiten por Bluetooth LE (el mismo que usa el iPhone
/// para enseñar la batería al abrir el estuche). Va en los datos de fabricante de Apple (0x004C), tipo 0x07.
/// </summary>
public static class AppleProximity
{
    public const ushort AppleCompanyId = 0x004C;
    const byte ProximityPairing = 0x07;

    /// <param name="data">Datos de fabricante sin el identificador de compañía.</param>
    public static PodStatus? Parse(ReadOnlySpan<byte> data)
    {
        // Solo el mensaje de batería: 07 19 01 … (27 bytes). Desconectados, los AirPods emiten también 07 11 06 …
        // (cifrado, el de "Buscar"), que no lleva la batería: leerlo como si la llevara da números al azar.
        if (data.Length < 27 || data[0] != ProximityPairing || data[1] != 0x19 || data[2] != 0x01) return null;

        ushort model = (ushort)((data[3] << 8) | data[4]);
        byte status = data[5];
        // El bit 5 dice cuál de los dos manda; según eso los nibbles de izquierdo y derecho van al revés
        bool flip = (status & 0x20) == 0;

        int hi = data[6] >> 4, lo = data[6] & 0x0F;
        int leftRaw = flip ? hi : lo;
        int rightRaw = flip ? lo : hi;
        int caseRaw = data[7] & 0x0F;
        int charge = data[7] >> 4;
        int ear = status & 0x0F;

        return new PodStatus(
            Model: model,
            Left: Percent(leftRaw),
            Right: Percent(rightRaw),
            Case: Percent(caseRaw),
            LeftCharging: (charge & (flip ? 0x02 : 0x01)) != 0,
            RightCharging: (charge & (flip ? 0x01 : 0x02)) != 0,
            CaseCharging: (charge & 0x04) != 0,
            LeftInEar: (ear & (flip ? 0x08 : 0x02)) != 0,
            RightInEar: (ear & (flip ? 0x02 : 0x08)) != 0);
    }

    // 0-10 son décimas de batería; 15 = desconocido
    static int? Percent(int raw) => raw <= 10 ? raw * 10 : null;

    public static string ModelName(ushort model) => model switch
    {
        0x0220 => "AirPods",
        0x0F20 => "AirPods (2.ª gen.)",
        0x1320 => "AirPods (3.ª gen.)",
        0x1920 or 0x1B20 => "AirPods 4",
        0x0E20 => "AirPods Pro",
        0x1420 or 0x2420 => "AirPods Pro 2",
        0x2720 => "AirPods Pro 3", // comprobado con unos A3064
        0x0A20 or 0x1F20 => "AirPods Max",
        _ => "AirPods",
    };

    /// <summary>Si conocemos el modelo (para no descartar por nombre unos AirPods de un modelo que no tenemos en la lista).</summary>
    public static bool IsKnownModel(ushort model) => ModelName(model) != "AirPods" || model == 0x0220;
}
