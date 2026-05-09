using OpenPort.Net.Internal;
using OpenPort.Net.Models;

namespace OpenPort.Net.Tests;

public class NatPmpMessageTests
{
    [Fact]
    public void CreateExternalAddressRequest_UsesVersionZeroAndOpcodeZero()
    {
        Assert.Equal([0, 0], NatPmpMessage.CreateExternalAddressRequest());
    }

    [Fact]
    public void CreateMapRequest_EncodesUdpPortsAndLifetimeInNetworkByteOrder()
    {
        var request = NatPmpMessage.CreateMapRequest(PortProtocol.Udp, 19132, 19133, 3600);

        Assert.Equal(12, request.Length);
        Assert.Equal(0, request[0]);
        Assert.Equal(1, request[1]);
        Assert.Equal([0x4a, 0xbc], request[4..6]);
        Assert.Equal([0x4a, 0xbd], request[6..8]);
        Assert.Equal([0x00, 0x00, 0x0e, 0x10], request[8..12]);
    }

    [Fact]
    public void TryParseMapResponse_ReturnsAssignedExternalPort()
    {
        var response = new byte[16];
        response[1] = 128 + NatPmpMessage.UdpMapOpcode;
        response[8] = 0x4a;
        response[9] = 0xbc;
        response[10] = 0x4a;
        response[11] = 0xbe;
        response[14] = 0x0e;
        response[15] = 0x10;

        var parsed = NatPmpMessage.TryParseMapResponse(response, PortProtocol.Udp, out var mapResponse);

        Assert.True(parsed);
        Assert.Equal(0, mapResponse.ResultCode);
        Assert.Equal(19132, mapResponse.InternalPort);
        Assert.Equal(19134, mapResponse.ExternalPort);
        Assert.Equal(3600u, mapResponse.LifetimeSeconds);
    }

    [Fact]
    public void MapResultCode_MapsKnownFailures()
    {
        Assert.Equal(OpenPortStatus.NotSupported, NatPmpMessage.MapResultCode(1));
        Assert.Equal(OpenPortStatus.Unauthorized, NatPmpMessage.MapResultCode(2));
        Assert.Equal(OpenPortStatus.NoResources, NatPmpMessage.MapResultCode(4));
        Assert.Equal(OpenPortStatus.NotSupported, NatPmpMessage.MapResultCode(5));
    }
}
