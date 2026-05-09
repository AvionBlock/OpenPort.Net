using System.Net;

namespace OpenPort.Net.PCP;

public class PcpNatDevice : NatDevice
{
    public override IPEndPoint HostEndPoint { get; }
    public override IPAddress LocalAddress { get; }
    
    public override Task OpenPort(Mapping mapping, CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}