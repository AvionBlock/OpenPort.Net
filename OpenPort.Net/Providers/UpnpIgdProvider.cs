using System.Net;
using System.Xml.Linq;
using OpenPort.Net.Discovery;
using OpenPort.Net.Internal;
using OpenPort.Net.Models;

namespace OpenPort.Net.Providers;

/// <summary>
/// UPnP Internet Gateway Device provider using SSDP discovery and SOAP WANIPConnection/WANPPPConnection actions.
/// </summary>
public sealed class UpnpIgdProvider : IPortMappingProvider
{
    private static readonly string[] SupportedServiceTypes =
    [
        "urn:schemas-upnp-org:service:WANIPConnection:2",
        "urn:schemas-upnp-org:service:WANIPConnection:1",
        "urn:schemas-upnp-org:service:WANPPPConnection:1"
    ];

    private readonly SsdpDiscovery _ssdpDiscovery;
    private readonly SoapClient _soapClient;
    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<Uri> _knownRootDeviceUris;
    private UpnpService? _service;

    /// <summary>
    /// Creates a UPnP IGD provider using SSDP discovery.
    /// </summary>
    public UpnpIgdProvider(TimeSpan timeout)
        : this(timeout, Array.Empty<Uri>())
    {
    }

    /// <summary>
    /// Creates a UPnP IGD provider with optional known root-device description URLs.
    /// </summary>
    public UpnpIgdProvider(TimeSpan timeout, IEnumerable<Uri> knownRootDeviceUris)
    {
        _ssdpDiscovery = new SsdpDiscovery(timeout);
        _soapClient = new SoapClient(timeout);
        _httpClient = new HttpClient { Timeout = timeout };
        _knownRootDeviceUris = knownRootDeviceUris?.ToArray() ?? Array.Empty<Uri>();
    }

    /// <inheritdoc />
    public string Name => "UPnP IGD";

    /// <inheritdoc />
    public async Task<bool> IsAvailableAsync(CancellationToken cancellationToken = default) =>
        _service is not null || await DiscoverAsync(cancellationToken).ConfigureAwait(false);

    /// <inheritdoc />
    public async Task<bool> DiscoverAsync(CancellationToken cancellationToken = default)
    {
        if (_service is not null)
        {
            return true;
        }

        foreach (var location in _knownRootDeviceUris)
        {
            var service = await TryLoadServiceAsync(location, cancellationToken).ConfigureAwait(false);
            if (service is not null)
            {
                _service = service;
                return true;
            }
        }

        var discoveredLocations = await _ssdpDiscovery.DiscoverInternetGatewayDevicesAsync(cancellationToken).ConfigureAwait(false);
        var seenLocations = new HashSet<string>(_knownRootDeviceUris.Select(uri => uri.AbsoluteUri), StringComparer.OrdinalIgnoreCase);
        foreach (var location in discoveredLocations.Where(location => seenLocations.Add(location.AbsoluteUri)))
        {
            var service = await TryLoadServiceAsync(location, cancellationToken).ConfigureAwait(false);
            if (service is not null)
            {
                _service = service;
                return true;
            }
        }

        return false;
    }

    /// <inheritdoc />
    public async Task<PortMappingResult> OpenAsync(PortMapping mapping, CancellationToken cancellationToken = default)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false) || _service is null)
        {
            return Result(OpenPortStatus.GatewayNotFound, mapping, "No UPnP IGD service was discovered.");
        }

        try
        {
            await _soapClient.InvokeAsync(
                _service.ControlUri,
                _service.ServiceType,
                "AddPortMapping",
                BuildAddArguments(mapping),
                cancellationToken).ConfigureAwait(false);

            return Result(OpenPortStatus.Success, mapping, externalPort: mapping.ExternalPort);
        }
        catch (UpnpSoapException ex)
        {
            return Result(
                UpnpErrorMapper.MapSoapError(ex.ErrorCode),
                mapping,
                $"{ex.Message} ({UpnpErrorMapper.GetErrorName(ex.ErrorCode)})");
        }
    }

    /// <inheritdoc />
    public async Task<PortMappingResult> CloseAsync(PortMapping mapping, CancellationToken cancellationToken = default)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false) || _service is null)
        {
            return Result(OpenPortStatus.GatewayNotFound, mapping, "No UPnP IGD service was discovered.");
        }

        try
        {
            await _soapClient.InvokeAsync(
                _service.ControlUri,
                _service.ServiceType,
                "DeletePortMapping",
                new[]
                {
                    Pair("NewRemoteHost", ""),
                    Pair("NewExternalPort", mapping.ExternalPort.ToString()),
                    Pair("NewProtocol", NetworkUtils.ToUpnpProtocol(mapping.Protocol))
                },
                cancellationToken).ConfigureAwait(false);

            return Result(OpenPortStatus.Success, mapping);
        }
        catch (UpnpSoapException ex)
        {
            return Result(
                UpnpErrorMapper.MapSoapError(ex.ErrorCode),
                mapping,
                $"{ex.Message} ({UpnpErrorMapper.GetErrorName(ex.ErrorCode)})");
        }
    }

    /// <inheritdoc />
    public Task<PortMappingResult> RenewAsync(PortMapping mapping, CancellationToken cancellationToken = default) =>
        OpenAsync(mapping, cancellationToken);

    /// <inheritdoc />
    public async Task<IPAddress?> GetExternalIPAddressAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false) || _service is null)
        {
            return null;
        }

        try
        {
            var document = await _soapClient.InvokeAsync(
                _service.ControlUri,
                _service.ServiceType,
                "GetExternalIPAddress",
                Array.Empty<KeyValuePair<string, string>>(),
                cancellationToken).ConfigureAwait(false);

            var value = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "NewExternalIPAddress")?.Value;
            return IPAddress.TryParse(value, out var address) ? address : null;
        }
        catch (UpnpSoapException)
        {
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<PortMapping>> GetMappingsAsync(CancellationToken cancellationToken = default)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false) || _service is null)
        {
            return Array.Empty<PortMapping>();
        }

        var mappings = new List<PortMapping>();

        for (var index = 0; index < 256; index++)
        {
            try
            {
                var document = await _soapClient.InvokeAsync(
                    _service.ControlUri,
                    _service.ServiceType,
                    "GetGenericPortMappingEntry",
                    new[] { Pair("NewPortMappingIndex", index.ToString()) },
                    cancellationToken).ConfigureAwait(false);

                var mapping = ParseGenericMapping(document);
                if (mapping is not null)
                {
                    mappings.Add(mapping);
                }
            }
            catch (UpnpSoapException ex) when (ex.ErrorCode is 713 or 714)
            {
                break;
            }
            catch (UpnpSoapException)
            {
                break;
            }
        }

        return mappings;
    }

    /// <summary>
    /// Gets one UPnP port mapping by external port and protocol when the gateway supports GetSpecificPortMappingEntry.
    /// </summary>
    public async Task<PortMapping?> GetSpecificPortMappingEntryAsync(
        PortMapping mapping,
        CancellationToken cancellationToken = default)
    {
        if (!await IsAvailableAsync(cancellationToken).ConfigureAwait(false) || _service is null)
        {
            return null;
        }

        try
        {
            var document = await _soapClient.InvokeAsync(
                _service.ControlUri,
                _service.ServiceType,
                "GetSpecificPortMappingEntry",
                new[]
                {
                    Pair("NewRemoteHost", ""),
                    Pair("NewExternalPort", mapping.ExternalPort.ToString()),
                    Pair("NewProtocol", NetworkUtils.ToUpnpProtocol(mapping.Protocol))
                },
                cancellationToken).ConfigureAwait(false);

            return ParseSpecificMapping(document, mapping.ExternalPort, mapping.Protocol);
        }
        catch (UpnpSoapException ex) when (ex.ErrorCode is 714)
        {
            return null;
        }
        catch (UpnpSoapException)
        {
            return null;
        }
    }

    private async Task<UpnpService?> TryLoadServiceAsync(Uri rootDeviceUri, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, rootDeviceUri);
            using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var xml = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
            var document = XmlUtils.Parse(xml);
            var urlBase = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "URLBase")?.Value;
            var baseUri = Uri.TryCreate(urlBase, UriKind.Absolute, out var parsedBaseUri)
                ? parsedBaseUri
                : rootDeviceUri;

            foreach (var serviceType in SupportedServiceTypes)
            {
                var service = document.Descendants()
                    .Where(e => e.Name.LocalName == "service")
                    .Select(e => new
                    {
                        ServiceType = e.Elements().FirstOrDefault(c => c.Name.LocalName == "serviceType")?.Value,
                        ControlUrl = e.Elements().FirstOrDefault(c => c.Name.LocalName == "controlURL")?.Value
                    })
                    .FirstOrDefault(s => string.Equals(s.ServiceType, serviceType, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(s.ControlUrl));

                if (service?.ControlUrl is null)
                {
                    continue;
                }

                return new UpnpService(serviceType, ResolveControlUri(baseUri, service.ControlUrl));
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Xml.XmlException or UriFormatException)
        {
            return null;
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return null;
    }

    private static Uri ResolveControlUri(Uri rootDeviceUri, string controlUrl)
    {
        if (Uri.TryCreate(controlUrl, UriKind.Absolute, out var absolute))
        {
            return absolute;
        }

        var baseUri = new Uri(rootDeviceUri.GetLeftPart(UriPartial.Authority));
        return new Uri(baseUri, controlUrl);
    }

    private static IEnumerable<KeyValuePair<string, string>> BuildAddArguments(PortMapping mapping)
    {
        var internalAddress = mapping.InternalAddress?.ToString() ?? NetworkUtils.GetLocalAddressForGateway(NetworkUtils.GetDefaultGatewayAddress() ?? IPAddress.None)?.ToString() ?? "";

        return new[]
        {
            Pair("NewRemoteHost", ""),
            Pair("NewExternalPort", mapping.ExternalPort.ToString()),
            Pair("NewProtocol", NetworkUtils.ToUpnpProtocol(mapping.Protocol)),
            Pair("NewInternalPort", mapping.InternalPort.ToString()),
            Pair("NewInternalClient", internalAddress),
            Pair("NewEnabled", "1"),
            Pair("NewPortMappingDescription", mapping.Description),
            Pair("NewLeaseDuration", NetworkUtils.ToSeconds(mapping.Lifetime).ToString())
        };
    }

    private static PortMapping? ParseGenericMapping(XDocument document)
    {
        string? Value(string name) => document.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

        if (!int.TryParse(Value("NewInternalPort"), out var internalPort) ||
            !int.TryParse(Value("NewExternalPort"), out var externalPort))
        {
            return null;
        }

        var protocol = string.Equals(Value("NewProtocol"), "TCP", StringComparison.OrdinalIgnoreCase)
            ? PortProtocol.Tcp
            : PortProtocol.Udp;

        var lifetime = uint.TryParse(Value("NewLeaseDuration"), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

        return new PortMapping
        {
            InternalPort = internalPort,
            ExternalPort = externalPort,
            Protocol = protocol,
            Description = Value("NewPortMappingDescription") ?? "",
            Lifetime = lifetime,
            InternalAddress = IPAddress.TryParse(Value("NewInternalClient"), out var address) ? address : null
        };
    }

    private static PortMapping? ParseSpecificMapping(XDocument document, int externalPort, PortProtocol protocol)
    {
        string? Value(string name) => document.Descendants().FirstOrDefault(e => e.Name.LocalName == name)?.Value;

        if (!int.TryParse(Value("NewInternalPort"), out var internalPort))
        {
            return null;
        }

        var lifetime = uint.TryParse(Value("NewLeaseDuration"), out var seconds)
            ? TimeSpan.FromSeconds(seconds)
            : TimeSpan.Zero;

        return new PortMapping
        {
            InternalPort = internalPort,
            ExternalPort = externalPort,
            Protocol = protocol,
            Description = Value("NewPortMappingDescription") ?? "",
            Lifetime = lifetime,
            InternalAddress = IPAddress.TryParse(Value("NewInternalClient"), out var address) ? address : null
        };
    }

    private static KeyValuePair<string, string> Pair(string key, string value) => new(key, value);

    private PortMappingResult Result(OpenPortStatus status, PortMapping mapping, string? error = null, int? externalPort = null) =>
        new()
        {
            Status = status,
            Provider = Name,
            Mapping = mapping,
            ExternalPort = externalPort,
            ErrorMessage = error
        };

    private sealed class UpnpService
    {
        public UpnpService(string serviceType, Uri controlUri)
        {
            ServiceType = serviceType;
            ControlUri = controlUri;
        }

        public string ServiceType { get; }
        public Uri ControlUri { get; }
    }
}
