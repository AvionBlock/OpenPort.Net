using System.Net;
using System.Net.Sockets;
using System.Text;

namespace OpenPort.Net.Tests;

internal sealed class UpnpTestServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serverTask;

    public UpnpTestServer()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var endpoint = (IPEndPoint)_listener.LocalEndpoint;
        RootUri = new Uri($"http://127.0.0.1:{endpoint.Port}/root.xml");
        _serverTask = RunAsync();
    }

    public Uri RootUri { get; }
    public List<string> SoapBodies { get; } = [];

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener.Stop();

        try
        {
            await _serverTask.ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
        }
        catch (OperationCanceledException)
        {
        }

        _stop.Dispose();
    }

    private async Task RunAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(_stop.Token).ConfigureAwait(false);
            _ = Task.Run(() => HandleClientAsync(client), _stop.Token);
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        {
            var stream = client.GetStream();
            var header = await ReadHeaderAsync(stream).ConfigureAwait(false);
            var contentLength = ReadContentLength(header);
            var bodyBytes = new byte[contentLength];
            var read = 0;
            while (read < contentLength)
            {
                var count = await stream.ReadAsync(bodyBytes.AsMemory(read, contentLength - read)).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }

                read += count;
            }

            var body = Encoding.UTF8.GetString(bodyBytes, 0, read);
            var path = header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[0].Split(' ')[1];

            if (path == "/root.xml")
            {
                await WriteResponseAsync(stream, BuildRootXml()).ConfigureAwait(false);
                return;
            }

            SoapBodies.Add(body);
            if (body.Contains("GetSpecificPortMappingEntry", StringComparison.Ordinal))
            {
                await WriteResponseAsync(stream, BuildSpecificMappingResponse()).ConfigureAwait(false);
                return;
            }

            if (body.Contains("GetExternalIPAddress", StringComparison.Ordinal))
            {
                await WriteResponseAsync(stream, BuildExternalAddressResponse()).ConfigureAwait(false);
                return;
            }

            await WriteResponseAsync(stream, BuildEmptySoapResponse()).ConfigureAwait(false);
        }
    }

    private string BuildRootXml() =>
        $"""
        <?xml version="1.0"?>
        <root xmlns="urn:schemas-upnp-org:device-1-0">
          <URLBase>{RootUri.GetLeftPart(UriPartial.Authority)}/</URLBase>
          <device>
            <serviceList>
              <service>
                <serviceType>urn:schemas-upnp-org:service:WANIPConnection:1</serviceType>
                <controlURL>/control</controlURL>
              </service>
            </serviceList>
          </device>
        </root>
        """;

    private static string BuildExternalAddressResponse() =>
        """
        <?xml version="1.0"?>
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
          <s:Body>
            <u:GetExternalIPAddressResponse xmlns:u="urn:schemas-upnp-org:service:WANIPConnection:1">
              <NewExternalIPAddress>203.0.113.30</NewExternalIPAddress>
            </u:GetExternalIPAddressResponse>
          </s:Body>
        </s:Envelope>
        """;

    private static string BuildSpecificMappingResponse() =>
        """
        <?xml version="1.0"?>
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
          <s:Body>
            <u:GetSpecificPortMappingEntryResponse xmlns:u="urn:schemas-upnp-org:service:WANIPConnection:1">
              <NewInternalPort>19132</NewInternalPort>
              <NewInternalClient>192.168.1.20</NewInternalClient>
              <NewEnabled>1</NewEnabled>
              <NewPortMappingDescription>Test App</NewPortMappingDescription>
              <NewLeaseDuration>3600</NewLeaseDuration>
            </u:GetSpecificPortMappingEntryResponse>
          </s:Body>
        </s:Envelope>
        """;

    private static string BuildEmptySoapResponse() =>
        """
        <?xml version="1.0"?>
        <s:Envelope xmlns:s="http://schemas.xmlsoap.org/soap/envelope/">
          <s:Body />
        </s:Envelope>
        """;

    private static async Task<string> ReadHeaderAsync(NetworkStream stream)
    {
        var bytes = new List<byte>();
        var buffer = new byte[1];

        while (true)
        {
            var read = await stream.ReadAsync(buffer).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            bytes.Add(buffer[0]);
            if (bytes.Count >= 4 &&
                bytes[^4] == '\r' &&
                bytes[^3] == '\n' &&
                bytes[^2] == '\r' &&
                bytes[^1] == '\n')
            {
                break;
            }
        }

        return Encoding.ASCII.GetString(bytes.ToArray());
    }

    private static int ReadContentLength(string header)
    {
        foreach (var line in header.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = line.IndexOf(':');
            if (separator <= 0)
            {
                continue;
            }

            if (string.Equals(line[..separator], "Content-Length", StringComparison.OrdinalIgnoreCase) &&
                int.TryParse(line[(separator + 1)..].Trim(), out var contentLength))
            {
                return contentLength;
            }
        }

        return 0;
    }

    private static async Task WriteResponseAsync(NetworkStream stream, string body)
    {
        var bodyBytes = Encoding.UTF8.GetBytes(body);
        var header = Encoding.ASCII.GetBytes(
            "HTTP/1.1 200 OK\r\n" +
            "Content-Type: text/xml; charset=utf-8\r\n" +
            $"Content-Length: {bodyBytes.Length}\r\n" +
            "Connection: close\r\n\r\n");

        await stream.WriteAsync(header).ConfigureAwait(false);
        await stream.WriteAsync(bodyBytes).ConfigureAwait(false);
    }
}
