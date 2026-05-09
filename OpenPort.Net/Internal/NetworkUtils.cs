using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace OpenPort.Net.Internal;

internal static class NetworkUtils
{
    public static IPAddress? GetDefaultGatewayAddress()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(nic => nic.OperationalStatus == OperationalStatus.Up)
            .SelectMany(nic => nic.GetIPProperties().GatewayAddresses)
            .Select(gateway => gateway.Address)
            .FirstOrDefault(address => address.AddressFamily == AddressFamily.InterNetwork && !address.Equals(IPAddress.Any));
    }

    public static IPAddress? GetLocalAddressForGateway(IPAddress gatewayAddress)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces().Where(nic => nic.OperationalStatus == OperationalStatus.Up))
        {
            var properties = nic.GetIPProperties();
            var hasGateway = properties.GatewayAddresses.Any(g => g.Address.Equals(gatewayAddress));
            if (!hasGateway)
            {
                continue;
            }

            var address = properties.UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            if (address is not null)
            {
                return address;
            }
        }

        return null;
    }

    public static string ToUpnpProtocol(Models.PortProtocol protocol) =>
        protocol == Models.PortProtocol.Tcp ? "TCP" : "UDP";

    public static uint ToSeconds(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.Zero)
        {
            return 0;
        }

        return lifetime.TotalSeconds >= uint.MaxValue ? uint.MaxValue : (uint)Math.Ceiling(lifetime.TotalSeconds);
    }

    public static void WriteUInt16BigEndian(byte[] buffer, int offset, int value)
    {
        buffer[offset] = (byte)(value >> 8);
        buffer[offset + 1] = (byte)value;
    }

    public static void WriteUInt32BigEndian(byte[] buffer, int offset, uint value)
    {
        buffer[offset] = (byte)(value >> 24);
        buffer[offset + 1] = (byte)(value >> 16);
        buffer[offset + 2] = (byte)(value >> 8);
        buffer[offset + 3] = (byte)value;
    }

    public static ushort ReadUInt16BigEndian(byte[] buffer, int offset) =>
        (ushort)((buffer[offset] << 8) | buffer[offset + 1]);

    public static uint ReadUInt32BigEndian(byte[] buffer, int offset) =>
        ((uint)buffer[offset] << 24) |
        ((uint)buffer[offset + 1] << 16) |
        ((uint)buffer[offset + 2] << 8) |
        buffer[offset + 3];
}
