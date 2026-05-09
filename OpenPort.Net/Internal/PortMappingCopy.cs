using OpenPort.Net.Models;

namespace OpenPort.Net.Internal;

internal static class PortMappingCopy
{
    public static PortMapping WithExternalPort(PortMapping mapping, int externalPort) =>
        new()
        {
            InternalPort = mapping.InternalPort,
            ExternalPort = externalPort,
            Protocol = mapping.Protocol,
            Description = mapping.Description,
            Lifetime = mapping.Lifetime,
            InternalAddress = mapping.InternalAddress
        };
}
