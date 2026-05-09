using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OpenPort.Net.Discovery;

internal sealed class SsdpDiscovery
{
    private static readonly IPEndPoint MulticastEndPoint = new(IPAddress.Parse("239.255.255.250"), 1900);
    private readonly TimeSpan _timeout;

    public SsdpDiscovery(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public async Task<IReadOnlyList<Uri>> DiscoverInternetGatewayDevicesAsync(CancellationToken cancellationToken)
    {
        var searchTargets = new[]
        {
            "urn:schemas-upnp-org:device:InternetGatewayDevice:1",
            "urn:schemas-upnp-org:service:WANIPConnection:1",
            "urn:schemas-upnp-org:service:WANIPConnection:2",
            "urn:schemas-upnp-org:service:WANPPPConnection:1"
        };

        var locations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var udpClient = new UdpClient(AddressFamily.InterNetwork);
        udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);

        foreach (var searchTarget in searchTargets)
        {
            var request = BuildSearchRequest(searchTarget);
            var bytes = Encoding.ASCII.GetBytes(request);
            await udpClient.SendAsync(bytes, bytes.Length, MulticastEndPoint).ConfigureAwait(false);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(_timeout);

        while (!timeout.IsCancellationRequested)
        {
            try
            {
                var response = await ReceiveAsync(udpClient, timeout.Token).ConfigureAwait(false);
                if (response is null)
                {
                    break;
                }

                var text = Encoding.ASCII.GetString(response.Value.Buffer);
                var location = ParseHeader(text, "LOCATION");
                if (Uri.TryCreate(location, UriKind.Absolute, out var uri))
                {
                    locations.Add(uri.AbsoluteUri);
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
        }

        return locations.Select(location => new Uri(location)).ToList();
    }

    private static string BuildSearchRequest(string searchTarget) =>
        "M-SEARCH * HTTP/1.1\r\n" +
        "HOST: 239.255.255.250:1900\r\n" +
        "MAN: \"ssdp:discover\"\r\n" +
        "MX: 2\r\n" +
        $"ST: {searchTarget}\r\n\r\n";

    private static string? ParseHeader(string response, string name)
    {
        foreach (var line in response.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            if (string.Equals(line[..separator].Trim(), name, StringComparison.OrdinalIgnoreCase))
            {
                return line[(separator + 1)..].Trim();
            }
        }

        return null;
    }

    private static async Task<UdpReceiveResult?> ReceiveAsync(UdpClient udpClient, CancellationToken cancellationToken)
    {
#if NET8_0_OR_GREATER
        return await udpClient.ReceiveAsync(cancellationToken).ConfigureAwait(false);
#else
        var receiveTask = udpClient.ReceiveAsync();
        var completed = await Task.WhenAny(receiveTask, Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken)).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        return completed == receiveTask ? receiveTask.Result : null;
#endif
    }
}
