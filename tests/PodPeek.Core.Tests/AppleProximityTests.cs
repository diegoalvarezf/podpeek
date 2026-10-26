using PodPeek.Core;
using Xunit;

namespace PodPeek.Core.Tests;

public class AppleProximityTests
{
    // 07 19 01 | modelo 0E 20 | estado | batería auriculares | carga + estuche | resto (tapa, color…)
    static byte[] Message(byte status, byte pods, byte chargeAndCase) =>
        new byte[] { 0x07, 0x19, 0x01, 0x0E, 0x20, status, pods, chargeAndCase, 0x00, 0x00, 0x00 };

    [Fact]
    public void Reads_battery_without_flip()
    {
        // bit 5 activo -> sin flip: nibble bajo = izquierdo, alto = derecho
        var s = AppleProximity.Parse(Message(status: 0x20, pods: 0x87, chargeAndCase: 0x04))!;
        Assert.Equal(70, s.Left);
        Assert.Equal(80, s.Right);
        Assert.Equal(40, s.Case);
        Assert.Equal("AirPods Pro", AppleProximity.ModelName(s.Model));
    }

    [Fact]
    public void Reads_battery_with_flip()
    {
        var s = AppleProximity.Parse(Message(status: 0x00, pods: 0x87, chargeAndCase: 0x0A))!;
        Assert.Equal(80, s.Left);
        Assert.Equal(70, s.Right);
        Assert.Equal(100, s.Case);
    }

    [Fact]
    public void Unknown_levels_are_null()
    {
        var s = AppleProximity.Parse(Message(status: 0x20, pods: 0xF5, chargeAndCase: 0x0F))!;
        Assert.Equal(50, s.Left);
        Assert.Null(s.Right);
        Assert.Null(s.Case);
        Assert.Equal(50, s.LowestPod);
    }

    [Fact]
    public void Reads_charging_flags()
    {
        // carga: bit 0 y bit 2 -> sin flip = izquierdo y estuche
        var s = AppleProximity.Parse(Message(status: 0x20, pods: 0x55, chargeAndCase: 0x55))!;
        Assert.True(s.LeftCharging);
        Assert.False(s.RightCharging);
        Assert.True(s.CaseCharging);
    }

    [Fact]
    public void Reads_in_ear_with_and_without_flip()
    {
        var noFlip = AppleProximity.Parse(Message(status: 0x22, pods: 0x55, chargeAndCase: 0x05))!;
        Assert.True(noFlip.LeftInEar);
        Assert.False(noFlip.RightInEar);

        var flip = AppleProximity.Parse(Message(status: 0x0A, pods: 0x55, chargeAndCase: 0x05))!;
        Assert.True(flip.BothInEar);
    }

    [Fact]
    public void Ignores_other_apple_messages()
    {
        Assert.Null(AppleProximity.Parse(new byte[] { 0x10, 0x05, 0x01, 0x18, 0x00, 0x00, 0x00, 0x00, 0x00 }));
        Assert.Null(AppleProximity.Parse(new byte[] { 0x07, 0x19 }));
    }
}
