using OpenPort.Net.Models;

namespace OpenPort.Net;

/// <summary>
/// Represents an opened port mapping that can renew itself and close on disposal.
/// </summary>
public sealed class OpenPortLease : IAsyncDisposable, IDisposable
{
    private readonly OpenPortClient _client;
    private readonly CancellationTokenSource _stopRenewal = new();
    private readonly Task? _renewalTask;
    private bool _disposed;

    internal OpenPortLease(OpenPortClient client, PortMappingResult result, bool autoRenew)
    {
        _client = client;
        Result = result;
        Mapping = result.Mapping ?? throw new ArgumentException("A successful result must contain a mapping.", nameof(result));

        if (autoRenew && Mapping.Lifetime > TimeSpan.Zero)
        {
            _renewalTask = RenewLoopAsync(_stopRenewal.Token);
        }
    }

    /// <summary>
    /// Gets the mapping managed by this lease.
    /// </summary>
    public PortMapping Mapping { get; }

    /// <summary>
    /// Gets the result returned by the operation that opened the lease.
    /// </summary>
    public PortMappingResult Result { get; }

    /// <summary>
    /// Stops renewal and closes the mapping.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _stopRenewal.Cancel();

        if (_renewalTask is not null)
        {
            try
            {
                await _renewalTask.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _client.CloseAsync(Mapping).ConfigureAwait(false);
        _stopRenewal.Dispose();
    }

    /// <summary>
    /// Stops renewal and closes the mapping synchronously.
    /// </summary>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    private async Task RenewLoopAsync(CancellationToken cancellationToken)
    {
        var delay = GetRenewDelay(Mapping.Lifetime);

        while (!cancellationToken.IsCancellationRequested)
        {
            await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            await _client.RenewAsync(Mapping, cancellationToken).ConfigureAwait(false);
        }
    }

    private static TimeSpan GetRenewDelay(TimeSpan lifetime)
    {
        if (lifetime <= TimeSpan.FromSeconds(10))
        {
            return TimeSpan.FromTicks(Math.Max(1, lifetime.Ticks / 2));
        }

        var preferredTicks = (long)(lifetime.Ticks * 0.8);
        var latestSafeTicks = lifetime.Ticks - TimeSpan.FromSeconds(5).Ticks;
        var delayTicks = Math.Min(preferredTicks, latestSafeTicks);
        return TimeSpan.FromTicks(Math.Max(TimeSpan.FromSeconds(5).Ticks, delayTicks));
    }
}
