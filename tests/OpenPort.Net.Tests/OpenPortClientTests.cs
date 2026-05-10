using System.Net;
using OpenPort.Net.Models;
using OpenPort.Net.Providers;

namespace OpenPort.Net.Tests;

public class OpenPortClientTests
{
    [Fact]
    public async Task OpenAsync_FallsBackUntilProviderSucceeds()
    {
        var unavailable = new FakeProvider("first", OpenPortStatus.NotSupported);
        var successful = new FakeProvider("second", OpenPortStatus.Success);
        var client = new OpenPortClient([unavailable, successful]);

        var result = await client.OpenAsync(NewMapping());

        Assert.Equal(OpenPortStatus.Success, result.Status);
        Assert.Equal("second", result.Provider);
        Assert.Equal(1, unavailable.OpenCalls);
        Assert.Equal(1, successful.OpenCalls);
    }

    [Fact]
    public async Task CloseAsync_UsesProviderThatOpenedMapping()
    {
        var first = new FakeProvider("first", OpenPortStatus.Success);
        var second = new FakeProvider("second", OpenPortStatus.Success);
        var client = new OpenPortClient([first, second]);
        var mapping = NewMapping();

        await client.OpenAsync(mapping);
        var closeResult = await client.CloseAsync(mapping);

        Assert.Equal(OpenPortStatus.Success, closeResult.Status);
        Assert.Equal(1, first.OpenCalls);
        Assert.Equal(1, first.CloseCalls);
        Assert.Equal(0, second.OpenCalls);
        Assert.Equal(0, second.CloseCalls);
    }

    [Fact]
    public async Task Constructor_UsesProvidersFromOptionsWhenSupplied()
    {
        var first = new FakeProvider("first", OpenPortStatus.Success);
        var second = new FakeProvider("second", OpenPortStatus.Success);
        var client = new OpenPortClient(new OpenPortOptions
        {
            Providers = [first, second],
            PreferredProtocols = [PortMappingProtocol.UpnpIgd]
        });

        var result = await client.OpenAsync(NewMapping());

        Assert.Equal(OpenPortStatus.Success, result.Status);
        Assert.Equal("first", result.Provider);
        Assert.Equal(1, first.OpenCalls);
        Assert.Equal(0, second.OpenCalls);
    }

    [Fact]
    public async Task CloseAsync_UsesOriginalMappingKeyWhenExternalPortChanged()
    {
        var first = new FakeProvider("first", OpenPortStatus.ExternalPortChanged, assignedExternalPort: 19133);
        var second = new FakeProvider("second", OpenPortStatus.Success);
        var client = new OpenPortClient([first, second]);
        var mapping = NewMapping();

        var openResult = await client.OpenAsync(mapping);
        var closeResult = await client.CloseAsync(mapping);

        Assert.Equal(OpenPortStatus.ExternalPortChanged, openResult.Status);
        Assert.Equal(OpenPortStatus.Success, closeResult.Status);
        Assert.Equal(1, first.CloseCalls);
        Assert.Equal(0, second.CloseCalls);
    }

    [Fact]
    public async Task RenewAsync_UsesProviderThatOpenedMapping()
    {
        var first = new FakeProvider("first", OpenPortStatus.Success);
        var second = new FakeProvider("second", OpenPortStatus.Success);
        var client = new OpenPortClient([first, second]);
        var mapping = NewMapping();

        await client.OpenAsync(mapping);
        var renewResult = await client.RenewAsync(mapping);

        Assert.Equal(OpenPortStatus.Success, renewResult.Status);
        Assert.Equal(1, first.RenewCalls);
        Assert.Equal(0, second.RenewCalls);
    }

    private static PortMapping NewMapping() =>
        new()
        {
            InternalPort = 19132,
            ExternalPort = 19132,
            Protocol = PortProtocol.Udp,
            InternalAddress = IPAddress.Parse("192.168.1.100")
        };

    private sealed class FakeProvider : IPortMappingProvider
    {
        private readonly OpenPortStatus _openStatus;
        private readonly int? _assignedExternalPort;

        public FakeProvider(string name, OpenPortStatus openStatus, int? assignedExternalPort = null)
        {
            Name = name;
            _openStatus = openStatus;
            _assignedExternalPort = assignedExternalPort;
        }

        public string Name { get; }
        public int OpenCalls { get; private set; }
        public int CloseCalls { get; private set; }
        public int RenewCalls { get; private set; }

        public Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public Task<PortMappingResult> OpenAsync(PortMapping mapping, CancellationToken cancellationToken = default)
        {
            OpenCalls++;
            return Task.FromResult(Result(_openStatus, mapping));
        }

        public Task<PortMappingResult> CloseAsync(PortMapping mapping, CancellationToken cancellationToken = default)
        {
            CloseCalls++;
            return Task.FromResult(Result(OpenPortStatus.Success, mapping));
        }

        public Task<PortMappingResult> RenewAsync(PortMapping mapping, CancellationToken cancellationToken = default)
        {
            RenewCalls++;
            return Task.FromResult(Result(OpenPortStatus.Success, mapping));
        }

        public Task<IPAddress?> GetExternalIPAddressAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IPAddress?>(IPAddress.Parse("203.0.113.10"));

        public Task<IReadOnlyList<PortMapping>> GetMappingsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PortMapping>>(Array.Empty<PortMapping>());

        public Task<bool> DiscoverAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        private PortMappingResult Result(OpenPortStatus status, PortMapping mapping) =>
            new()
            {
                Status = status,
                Provider = Name,
                Mapping = status is OpenPortStatus.Success or OpenPortStatus.ExternalPortChanged
                    ? WithExternalPort(mapping, _assignedExternalPort ?? mapping.ExternalPort)
                    : null,
                ExternalPort = _assignedExternalPort ?? mapping.ExternalPort
            };

        private static PortMapping WithExternalPort(PortMapping mapping, int externalPort) =>
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
}
