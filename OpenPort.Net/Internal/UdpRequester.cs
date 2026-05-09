using System.Net;
using System.Net.Sockets;

namespace OpenPort.Net.Internal;

internal sealed class UdpRequester
{
    private readonly TimeSpan _timeout;

    public UdpRequester(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    public async Task<byte[]?> SendAsync(
        IPEndPoint remoteEndPoint,
        byte[] request,
        int attempts,
        CancellationToken cancellationToken)
    {
        Exception? lastSocketError = null;

        for (var attempt = 0; attempt < attempts; attempt++)
        {
            using var udpClient = new UdpClient(AddressFamily.InterNetwork);
            using var attemptToken = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = TimeSpan.FromMilliseconds(Math.Min(_timeout.TotalMilliseconds, 250 * Math.Pow(2, attempt)));
            attemptToken.CancelAfter(delay);

            try
            {
                await udpClient.SendAsync(request, request.Length, remoteEndPoint).ConfigureAwait(false);
                var response = await ReceiveAsync(udpClient, attemptToken.Token).ConfigureAwait(false);
                if (response is not null)
                {
                    return response.Value.Buffer;
                }
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
            }
            catch (SocketException ex)
            {
                lastSocketError = ex;
            }
        }

        if (lastSocketError is not null)
        {
            throw lastSocketError;
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
