using System.Net;

namespace OpenPort.Net;

public abstract class NatDevice
{
    public abstract IPEndPoint HostEndPoint { get; }
    public abstract IPAddress LocalAddress { get; }

    public abstract Task OpenPort(Mapping mapping, CancellationToken cancellationToken);
}