namespace OpenPort.Net;

public abstract class PortMapper
{
    public abstract Task<NatDevice?> Discover(CancellationToken cancellationToken);
    public abstract Task<List<NatDevice>> DiscoverAll(CancellationToken cancellationToken);
}