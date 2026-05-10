# OpenPort.Net

OpenPort.Net is a lightweight cross-platform .NET library for automatic NAT port mapping.
It lets desktop apps, game servers, voice chat servers, P2P apps and self-hosted tools open router ports automatically using UPnP IGD, NAT-PMP and PCP.
No external binaries are required.

## Install

```bash
dotnet add package OpenPort.Net
```

Target frameworks:

- `net8.0`
- `netstandard2.1`

Current package version: `1.0.0`.

## Quick Start

```csharp
using System.Net;
using OpenPort.Net;
using OpenPort.Net.Models;

var client = new OpenPortClient();

var result = await client.OpenAsync(new PortMapping
{
    InternalPort = 19132,
    ExternalPort = 19132,
    Protocol = PortProtocol.Udp,
    Description = "My App",
    Lifetime = TimeSpan.FromHours(1)
});

if (result.Status is OpenPortStatus.Success or OpenPortStatus.ExternalPortChanged)
{
    Console.WriteLine($"Opened with {result.Provider} on external port {result.ExternalPort}");
    await client.CloseAsync(result.Mapping!);
}
```

For automatic renew and cleanup:

```csharp
await using var lease = await client.OpenLeaseAsync(new PortMapping
{
    InternalPort = 19132,
    ExternalPort = 19132,
    Protocol = PortProtocol.Udp,
    Description = "My App",
    Lifetime = TimeSpan.FromHours(1)
});
```

`OpenPortLease` renews temporary mappings and closes the mapping when disposed.

## Provider Customization

The default order is:

1. PCP
2. NAT-PMP
3. UPnP IGD

For full customisation, pass provider instances. This controls both order and implementation:

```csharp
using OpenPort.Net.Providers;

var client = new OpenPortClient(new OpenPortOptions
{
    Providers =
    [
        new UpnpIgdProvider(TimeSpan.FromSeconds(5)),
        new NatPmpProvider(TimeSpan.FromSeconds(5)),
        new PcpProvider(TimeSpan.FromSeconds(5))
    ],
    Timeout = TimeSpan.FromSeconds(5)
});
```

You can also implement `IPortMappingProvider` and put your own provider in the list.

For simple built-in provider reordering, `PreferredProtocols` is still available:

```csharp
var client = new OpenPortClient(new OpenPortOptions
{
    PreferredProtocols =
    [
        PortMappingProtocol.UpnpIgd,
        PortMappingProtocol.NatPmp,
        PortMappingProtocol.Pcp
    ]
});
```

If you already know the gateway or UPnP root device URL, provide hints:

```csharp
var client = new OpenPortClient(new OpenPortOptions
{
    GatewayAddress = IPAddress.Parse("192.168.1.1"),
    UpnpRootDeviceUris = [new Uri("http://192.168.1.1:5000/root.xml")]
});
```

## Behavior

- All operations are async and accept `CancellationToken`.
- A mapping is not considered open until the gateway reports success.
- If a gateway assigns a different external port, the result status is `ExternalPortChanged` and `result.Mapping` contains the assigned port.
- Mappings opened by an `OpenPortClient` instance are renewed and closed through the same provider that opened them.
- UPnP supports `WANIPConnection:1`, `WANIPConnection:2` and `WANPPPConnection:1`.
- NAT-PMP supports external address lookup, TCP/UDP map, renew and delete.
- PCP supports ANNOUNCE and MAP for TCP/UDP.

## Sample

```bash
dotnet run --project samples/OpenPort.Net.Sample -- 19132 19132 udp
```

## Development

```bash
dotnet build OpenPort.Net.sln
dotnet test OpenPort.Net.sln
dotnet pack OpenPort.Net/OpenPort.Net.csproj -c Release -o artifacts
```

The test suite includes protocol codec tests and mocked UPnP, NAT-PMP and PCP routers.

## License

MIT
