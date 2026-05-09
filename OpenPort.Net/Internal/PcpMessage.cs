using System.Net;
using System.Security.Cryptography;
using OpenPort.Net.Models;

namespace OpenPort.Net.Internal;

internal static class PcpMessage
{
    public const byte Version = 2;
    public const byte AnnounceOpcode = 0;
    public const byte MapOpcode = 1;
    public const byte ResponseMask = 0x80;

    public static byte[] CreateAnnounceRequest(IPAddress clientAddress) =>
        BuildHeader(AnnounceOpcode, 0, clientAddress);

    public static byte[] CreateMapRequest(
        PortProtocol protocol,
        int internalPort,
        int suggestedExternalPort,
        uint lifetimeSeconds,
        IPAddress clientAddress,
        byte[]? nonce = null)
    {
        var request = BuildHeader(MapOpcode, lifetimeSeconds, clientAddress, 60);
        var mappingNonce = nonce ?? CreateNonce();
        if (mappingNonce.Length != 12)
        {
            throw new ArgumentException("A PCP mapping nonce must be exactly 12 bytes.", nameof(nonce));
        }

        Buffer.BlockCopy(mappingNonce, 0, request, 24, 12);
        request[36] = protocol == PortProtocol.Tcp ? (byte)6 : (byte)17;
        NetworkUtils.WriteUInt16BigEndian(request, 40, internalPort);
        NetworkUtils.WriteUInt16BigEndian(request, 42, suggestedExternalPort);
        WritePcpAddress(request, 44, IPAddress.Any);
        return request;
    }

    public static bool IsSuccessAnnounceResponse(byte[] response) =>
        response.Length >= 24 &&
        response[0] == Version &&
        (response[1] & ResponseMask) != 0 &&
        (response[1] & 0x7f) == AnnounceOpcode &&
        response[3] == 0;

    public static bool TryParseMapResponse(byte[] response, out PcpMapResponse mapResponse)
    {
        mapResponse = default;

        if (response.Length < 24 ||
            response[0] != Version ||
            (response[1] & ResponseMask) == 0 ||
            (response[1] & 0x7f) != MapOpcode)
        {
            return false;
        }

        var resultCode = response[3];
        if (resultCode != 0)
        {
            mapResponse = new PcpMapResponse(resultCode, 0, null, 0);
            return true;
        }

        if (response.Length < 60)
        {
            return false;
        }

        var assignedAddress = new IPAddress(response.Skip(44).Take(16).ToArray());
        if (assignedAddress.IsIPv4MappedToIPv6)
        {
            assignedAddress = assignedAddress.MapToIPv4();
        }

        mapResponse = new PcpMapResponse(
            resultCode,
            NetworkUtils.ReadUInt16BigEndian(response, 42),
            assignedAddress,
            NetworkUtils.ReadUInt32BigEndian(response, 4));
        return true;
    }

    public static OpenPortStatus MapResultCode(byte code) =>
        code switch
        {
            0 => OpenPortStatus.Success,
            1 => OpenPortStatus.NotSupported,
            2 => OpenPortStatus.Unauthorized,
            3 => OpenPortStatus.InvalidRequest,
            4 or 5 => OpenPortStatus.NotSupported,
            6 => OpenPortStatus.InvalidRequest,
            7 => OpenPortStatus.Failed,
            8 => OpenPortStatus.NoResources,
            9 => OpenPortStatus.NotSupported,
            10 => OpenPortStatus.NoResources,
            11 => OpenPortStatus.Conflict,
            12 or 13 => OpenPortStatus.InvalidRequest,
            _ => OpenPortStatus.Failed
        };

    public static string GetResultName(byte code) =>
        code switch
        {
            0 => "Success",
            1 => "UnsupportedVersion",
            2 => "NotAuthorized",
            3 => "MalformedRequest",
            4 => "UnsupportedOpcode",
            5 => "UnsupportedOption",
            6 => "MalformedOption",
            7 => "NetworkFailure",
            8 => "NoResources",
            9 => "UnsupportedProtocol",
            10 => "UserExceededQuota",
            11 => "CannotProvideExternal",
            12 => "AddressMismatch",
            13 => "ExcessiveRemotePeers",
            _ => "Unknown"
        };

    private static byte[] BuildHeader(byte opcode, uint lifetimeSeconds, IPAddress clientAddress, int length = 24)
    {
        var request = new byte[length];
        request[0] = Version;
        request[1] = opcode;
        NetworkUtils.WriteUInt32BigEndian(request, 4, lifetimeSeconds);
        WritePcpAddress(request, 8, clientAddress);
        return request;
    }

    public static byte[] CreateNonce()
    {
        var nonce = new byte[12];
        using var rng = RandomNumberGenerator.Create();
        rng.GetBytes(nonce);
        return nonce;
    }

    private static void WritePcpAddress(byte[] buffer, int offset, IPAddress address)
    {
        var bytes = address.MapToIPv6().GetAddressBytes();
        Buffer.BlockCopy(bytes, 0, buffer, offset, 16);
    }
}

internal readonly struct PcpMapResponse
{
    public PcpMapResponse(byte resultCode, int externalPort, IPAddress? externalAddress, uint lifetimeSeconds)
    {
        ResultCode = resultCode;
        ExternalPort = externalPort;
        ExternalAddress = externalAddress;
        LifetimeSeconds = lifetimeSeconds;
    }

    public byte ResultCode { get; }
    public int ExternalPort { get; }
    public IPAddress? ExternalAddress { get; }
    public uint LifetimeSeconds { get; }
}
