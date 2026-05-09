using System.Net;
using OpenPort.Net.Models;
using OpenPort.Net.Providers;

namespace OpenPort.Net.Tests;

public class UpnpIgdProviderTests
{
    [Fact]
    public async Task OpenAsync_LoadsKnownRootDeviceAndSendsAddPortMappingSoap()
    {
        await using var server = new UpnpTestServer();
        var provider = new UpnpIgdProvider(TimeSpan.FromSeconds(1), [server.RootUri]);

        var result = await provider.OpenAsync(new PortMapping
        {
            InternalPort = 19132,
            ExternalPort = 19132,
            Protocol = PortProtocol.Udp,
            Description = "Test App",
            Lifetime = TimeSpan.FromHours(1),
            InternalAddress = IPAddress.Parse("192.168.1.20")
        });

        Assert.Equal(OpenPortStatus.Success, result.Status);
        var body = Assert.Single(server.SoapBodies);
        Assert.Contains("AddPortMapping", body);
        Assert.Contains("<NewExternalPort>19132</NewExternalPort>", body);
        Assert.Contains("<NewProtocol>UDP</NewProtocol>", body);
        Assert.Contains("<NewInternalClient>192.168.1.20</NewInternalClient>", body);
        Assert.Contains("<NewLeaseDuration>3600</NewLeaseDuration>", body);
    }

    [Fact]
    public async Task GetExternalIPAddressAsync_ReadsSoapAddress()
    {
        await using var server = new UpnpTestServer();
        var provider = new UpnpIgdProvider(TimeSpan.FromSeconds(1), [server.RootUri]);

        var address = await provider.GetExternalIPAddressAsync();

        Assert.Equal(IPAddress.Parse("203.0.113.30"), address);
    }

    [Fact]
    public async Task GetSpecificPortMappingEntryAsync_ReadsSpecificMapping()
    {
        await using var server = new UpnpTestServer();
        var provider = new UpnpIgdProvider(TimeSpan.FromSeconds(1), [server.RootUri]);

        var mapping = await provider.GetSpecificPortMappingEntryAsync(new PortMapping
        {
            InternalPort = 19132,
            ExternalPort = 19132,
            Protocol = PortProtocol.Udp
        });

        Assert.NotNull(mapping);
        Assert.Equal(19132, mapping.InternalPort);
        Assert.Equal(19132, mapping.ExternalPort);
        Assert.Equal(PortProtocol.Udp, mapping.Protocol);
        Assert.Equal(IPAddress.Parse("192.168.1.20"), mapping.InternalAddress);
        Assert.Equal(TimeSpan.FromHours(1), mapping.Lifetime);
    }
}
