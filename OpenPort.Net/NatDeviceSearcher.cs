using OpenPort.Net.PCP;

namespace OpenPort.Net;

public class NatDeviceSearcher
{
    private readonly List<PortMapper> _portMappers =
    [
        new PcpPortMapper()
    ];
    
    public async Task<NatDevice?> DiscoverDeviceAsync(CancellationToken cancellationToken)
    {
        NatDevice? device = null;
        foreach (var portMapper in _portMappers.TakeWhile(_ => !cancellationToken.IsCancellationRequested))
        {
            try
            {
                device = await portMapper.Discover(cancellationToken);
            }
            catch
            {
                //Go Next
            }
        }

        return device;
    }
}