using LuminaVault.ServiceDefaults;
using NATS.Client.JetStream;

namespace LuminaVault.WebUI.Services;

/// <summary>
/// Retrieves JetStream consumer statistics for the media pipeline.
/// Used by the Warteschlange (queue overview) page.
/// </summary>
public sealed class NatsQueueService(INatsJSContext js)
{
    /// <summary>
    /// Returns queue stats for every known JetStream consumer.
    /// Each entry covers jobs that are pending (bereitstehend), in-progress (laufend),
    /// and completed (fertig).
    /// </summary>
    public async Task<IReadOnlyList<ConsumerQueueInfo>> GetConsumerStatsAsync(CancellationToken ct = default)
    {
        var consumerNames = new[]
        {
            NatsConsumers.ThumbnailGeneration,
            NatsConsumers.ObjectRecognition,
        };

        var results = new List<ConsumerQueueInfo>(consumerNames.Length);

        foreach (var name in consumerNames)
        {
            try
            {
                var consumer = await js.GetConsumerAsync(NatsStreams.MediaPipeline, name, ct);
                var info = consumer.Info;

                // NumPending   = messages in the stream not yet delivered to this consumer
                // NumAckPending = messages delivered but not yet acknowledged (in-flight)
                // Fertig (done) = total deliveries so far minus what is still in-flight
                var numPending = (long)info.NumPending;
                var numAckPending = info.NumAckPending;
                var numCompleted = (long)Math.Max(0UL, info.Delivered.ConsumerSeq - (ulong)Math.Max(0, numAckPending));

                results.Add(new ConsumerQueueInfo
                {
                    ConsumerName = name,
                    NumPending = numPending,
                    NumAckPending = numAckPending,
                    NumCompleted = numCompleted,
                    IsAvailable = true,
                });
            }
            catch (Exception ex)
            {
                results.Add(new ConsumerQueueInfo
                {
                    ConsumerName = name,
                    IsAvailable = false,
                    Error = ex.Message,
                });
            }
        }

        return results;
    }
}

/// <summary>Queue statistics for a single JetStream consumer.</summary>
public sealed class ConsumerQueueInfo
{
    /// <summary>The durable consumer name (e.g. "thumbnail-generation").</summary>
    public required string ConsumerName { get; init; }

    /// <summary>Messages in the stream that have not yet been delivered (bereitstehend).</summary>
    public long NumPending { get; init; }

    /// <summary>Messages delivered but not yet acknowledged — currently being processed (laufend).</summary>
    public int NumAckPending { get; init; }

    /// <summary>Approximate number of messages that have been processed (fertig).</summary>
    public long NumCompleted { get; init; }

    /// <summary>Whether the consumer info could be retrieved successfully.</summary>
    public bool IsAvailable { get; init; }

    /// <summary>Error message if <see cref="IsAvailable"/> is <c>false</c>.</summary>
    public string? Error { get; init; }
}
