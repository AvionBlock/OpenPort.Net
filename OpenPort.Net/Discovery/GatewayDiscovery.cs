using System.Net;
using OpenPort.Net.Internal;

namespace OpenPort.Net.Discovery;

internal sealed class GatewayDiscovery
{
    public IPAddress? DiscoverGatewayAddress() => NetworkUtils.GetDefaultGatewayAddress();
}
