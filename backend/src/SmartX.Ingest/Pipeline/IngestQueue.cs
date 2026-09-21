using System.Threading.Channels;
using Microsoft.Extensions.Options;

namespace SmartX.Ingest.Pipeline;

public enum EnqueueResult
{
    Accepted,
    Saturated,
    Closed
}


/// The boundary between "a request is on a thread pool thread" and "a reading is the
/// pipeline's problem". Single reader, many writers.
public sealed class IngestQueue
{
    private readonly Channel<IngestEnvelope> _channel;
    private readonly IngestOptions _options;
    private long _depth;

    public IngestQueue(IOptions<IngestOptions> options)
    {
        _options = options.Value;
        _channel = Channel.CreateBounded<IngestEnvelope>(new BoundedChannelOptions(_options.QueueCapacity)
        {
            // Wait rather than DropOldest: shedding at the edge with a 429 tells the device
            // to back off. Silently dropping from the middle of the queue would leave the
            // console showing a continuous series that has holes in it.
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
            AllowSynchronousContinuations = false
        });
    }

    public ChannelReader<IngestEnvelope> Reader => _channel.Reader;

    public long Depth => Interlocked.Read(ref _depth);

    public int Capacity => _options.QueueCapacity;

    public double Utilisation => Capacity == 0 ? 0 : (double)Depth / Capacity;

 
    /// Fast path is a non-blocking TryWrite. Only when the queue is genuinely full do we
    /// pay for an await, and then only for the configured grace period.
    public async ValueTask<EnqueueResult> EnqueueAsync(IngestEnvelope envelope, CancellationToken ct)
    {
        if (_channel.Writer.TryWrite(envelope))
        {
            Interlocked.Increment(ref _depth);
            return EnqueueResult.Accepted;
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_options.EnqueueTimeoutMs);

        try
        {
            while (await _channel.Writer.WaitToWriteAsync(timeout.Token).ConfigureAwait(false))
            {
                if (_channel.Writer.TryWrite(envelope))
                {
                    Interlocked.Increment(ref _depth);
                    return EnqueueResult.Accepted;
                }
            }

            return EnqueueResult.Closed;
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return EnqueueResult.Saturated;
        }
        catch (ChannelClosedException)
        {
            return EnqueueResult.Closed;
        }
    }

    internal void OnDequeued(int count = 1) => Interlocked.Add(ref _depth, -count);

    public void Complete() => _channel.Writer.TryComplete();
}
