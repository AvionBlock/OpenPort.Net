namespace OpenPort.Net.PCP;

public class PcpPortMapper : PortMapper
{
    public override Task<NatDevice?> Discover(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }

    public override Task<List<NatDevice>> DiscoverAll(CancellationToken cancellationToken)
    {
        throw new NotImplementedException();
    }
}