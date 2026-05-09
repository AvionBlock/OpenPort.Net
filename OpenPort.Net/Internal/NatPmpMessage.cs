using System.Net;
using OpenPort.Net.Models;

namespace OpenPort.Net.Internal;

internal static class NatPmpMessage
{
    public const byte Version = 0;
    public const byte ExternalAddressOpcode = 0;
    public const byte UdpMapOpcode = 1;
    public const byte TcpMapOpcode = 2;
    public const byte ResponseOffset = 128;

    public static byte[] CreateExternalAddressRequest() => [Version, ExternalAddressOpcode];

    public static byte[] CreateMapRequest(PortProtocol protocol, int internalPort, int externalPort, uint lifetimeSeconds)
    {
        var request = new byte[12];
        request[0] = Version;
        request[1] = ToMapOpcode(protocol);
        NetworkUtils.WriteUInt16BigEndian(request, 4, internalPort);
        NetworkUtils.WriteUInt16BigEndian(request, 6, externalPort);
        NetworkUtils.WriteUInt32BigEndian(request, 8, lifetimeSeconds);
        return request;
    }

    public static bool TryParseExternalAddressResponse(
        byte[] response,
        out ushort resultCode,
        out IPAddress? externalAddress)
    {
        resultCode = 0;
        externalAddress = null;

        if (response.Length < 8 || response[0] != Version || response[1] != ExternalAddressOpcode + ResponseOffset)
        {
            return false;
        }

        resultCode = NetworkUtils.ReadUInt16BigEndian(response, 2);
        if (resultCode != 0)
        {
            return true;
        }

        if (response.Length < 12)
        {
            return false;
        }

        externalAddress = new IPAddress(response.Skip(8).Take(4).ToArray());
        return true;
    }

    public static bool TryParseMapResponse(
        byte[] response,
        PortProtocol protocol,
        out NatPmpMapResponse mapResponse)
    {
        mapResponse = default;
        var opcode = ToMapOpcode(protocol);

        if (response.Length < 8 || response[0] != Version || response[1] != opcode + ResponseOffset)
        {
            return false;
        }

        var resultCode = NetworkUtils.ReadUInt16BigEndian(response, 2);
        if (resultCode != 0)
        {
            mapResponse = new NatPmpMapResponse(resultCode, 0, 0, 0);
            return true;
        }

        if (response.Length < 16)
        {
            return false;
        }

        mapResponse = new NatPmpMapResponse(
            resultCode,
            NetworkUtils.ReadUInt16BigEndian(response, 8),
            NetworkUtils.ReadUInt16BigEndian(response, 10),
            NetworkUtils.ReadUInt32BigEndian(response, 12));
        return true;
    }

    public static OpenPortStatus MapResultCode(ushort code) =>
        code switch
        {
            0 => OpenPortStatus.Success,
            1 => OpenPortStatus.NotSupported,
            2 => OpenPortStatus.Unauthorized,
            3 => OpenPortStatus.Failed,
            4 => OpenPortStatus.NoResources,
            5 => OpenPortStatus.NotSupported,
            _ => OpenPortStatus.Failed
        };

    public static string GetResultName(ushort code) =>
        code switch
        {
            0 => "Success",
            1 => "UnsupportedVersion",
            2 => "NotAuthorized",
            3 => "NetworkFailure",
            4 => "OutOfResources",
            5 => "UnsupportedOpcode",
            _ => "Unknown"
        };

    private static byte ToMapOpcode(PortProtocol protocol) =>
        protocol == PortProtocol.Udp ? UdpMapOpcode : TcpMapOpcode;
}

internal readonly struct NatPmpMapResponse
{
    public NatPmpMapResponse(ushort resultCode, int internalPort, int externalPort, uint lifetimeSeconds)
    {
        ResultCode = resultCode;
        InternalPort = internalPort;
        ExternalPort = externalPort;
        LifetimeSeconds = lifetimeSeconds;
    }

    public ushort ResultCode { get; }
    public int InternalPort { get; }
    public int ExternalPort { get; }
    public uint LifetimeSeconds { get; }
}
