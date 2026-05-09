using System.Net;
using OpenPort.Net.Internal;
using OpenPort.Net.Models;

namespace OpenPort.Net.Tests;

public class PcpMessageTests
{
    [Fact]
    public void CreateMapRequest_EncodesMapOpcodeProtocolPortsAndNonce()
    {
        var nonce = Enumerable.Range(0, 12).Select(i => (byte)(i + 1)).ToArray();
        var request = PcpMessage.CreateMapRequest(
            PortProtocol.Tcp,
            internalPort: 25565,
            suggestedExternalPort: 25566,
            lifetimeSeconds: 7200,
            clientAddress: IPAddress.Parse("192.168.1.10"),
            nonce: nonce);

        Assert.Equal(60, request.Length);
        Assert.Equal(2, request[0]);
        Assert.Equal(1, request[1]);
        Assert.Equal([0x00, 0x00, 0x1c, 0x20], request[4..8]);
        Assert.Equal(nonce, request[24..36]);
        Assert.Equal(6, request[36]);
        Assert.Equal([0x63, 0xdd], request[40..42]);
        Assert.Equal([0x63, 0xde], request[42..44]);
    }

    [Fact]
    public void TryParseMapResponse_ReturnsAssignedPortAndIPv4MappedExternalAddress()
    {
        var response = new byte[60];
        response[0] = PcpMessage.Version;
        response[1] = PcpMessage.ResponseMask | PcpMessage.MapOpcode;
        response[6] = 0x1c;
        response[7] = 0x20;
        response[42] = 0x63;
        response[43] = 0xdf;
        var mappedAddress = IPAddress.Parse("203.0.113.15").MapToIPv6().GetAddressBytes();
        Buffer.BlockCopy(mappedAddress, 0, response, 44, mappedAddress.Length);

        var parsed = PcpMessage.TryParseMapResponse(response, out var mapResponse);

        Assert.True(parsed);
        Assert.Equal(0, mapResponse.ResultCode);
        Assert.Equal(25567, mapResponse.ExternalPort);
        Assert.Equal(IPAddress.Parse("203.0.113.15"), mapResponse.ExternalAddress);
        Assert.Equal(7200u, mapResponse.LifetimeSeconds);
    }

    [Fact]
    public void MapResultCode_MapsPcpFailures()
    {
        Assert.Equal(OpenPortStatus.NotSupported, PcpMessage.MapResultCode(1));
        Assert.Equal(OpenPortStatus.Unauthorized, PcpMessage.MapResultCode(2));
        Assert.Equal(OpenPortStatus.InvalidRequest, PcpMessage.MapResultCode(3));
        Assert.Equal(OpenPortStatus.NoResources, PcpMessage.MapResultCode(8));
        Assert.Equal(OpenPortStatus.Conflict, PcpMessage.MapResultCode(11));
    }
}
