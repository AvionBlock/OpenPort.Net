using System.Net;
using System.Net.Sockets;

namespace OpenPort.Net.Tests;

internal sealed class UdpTestServer : IAsyncDisposable
{
    private readonly UdpClient _udpClient;
    private readonly Func<byte[], byte[]?> _handler;
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _serverTask;

    public UdpTestServer(Func<byte[], byte[]?> handler)
    {
        _handler = handler;
        _udpClient = new UdpClient(new IPEndPoint(IPAddress.Loopback, 0));
        EndPoint = (IPEndPoint)_udpClient.Client.LocalEndPoint!;
        _serverTask = RunAsync();
    }

    public IPEndPoint EndPoint { get; }
    public List<byte[]> Requests { get; } = [];

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _udpClient.Dispose();

        try
        {
            await _serverTask.ConfigureAwait(false);
        }
        catch (ObjectDisposedException)
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
            var request = await _udpClient.ReceiveAsync(_stop.Token).ConfigureAwait(false);
            Requests.Add(request.Buffer);
            var response = _handler(request.Buffer);
            if (response is not null)
            {
                await _udpClient.SendAsync(response, response.Length, request.RemoteEndPoint).ConfigureAwait(false);
            }
        }
    }
}
