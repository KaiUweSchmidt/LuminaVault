using LuminaVault.ServiceDefaults;
using Microsoft.EntityFrameworkCore;
using NATS.Client.Core;

namespace LuminaVault.MetadataStorage;

/// <summary>
/// Background service that subscribes to <see cref="NatsSubjects.PipelineStepCompleted"/> events
/// and persists the processing outcome per media item and step into the database.
/// </summary>
public sealed class NatsPipelineStatusSubscriber(
    INatsConnection nats,
    IServiceScopeFactory scopeFactory,
    ILogger<NatsPipelineStatusSubscriber> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("[NATS:PipelineStatus] Subscribing to {Subject}", NatsSubjects.PipelineStepCompleted);

        try
        {
            await foreach (var msg in nats.SubscribeAsync<PipelineStepCompletedEvent>(
                NatsSubjects.PipelineStepCompleted, cancellationToken: stoppingToken))
            {
                if (msg.Data is not { } evt)
                    continue;

                try
                {
                    await UpsertStatusAsync(evt, stoppingToken);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "[NATS:PipelineStatus] Fehler beim Speichern des Status für MediaId={MediaId}, Step={Step}",
                        evt.MediaId, evt.StepName);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Expected on shutdown
        }

        logger.LogInformation("[NATS:PipelineStatus] Subscriber gestoppt");
    }

    private async Task UpsertStatusAsync(PipelineStepCompletedEvent evt, CancellationToken ct)
    {
        await using var scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<MetadataDbContext>();

        var existing = await db.PipelineStatuses
            .FirstOrDefaultAsync(s => s.MediaId == evt.MediaId && s.StepName == evt.StepName, ct);

        var newStatus = evt.Success ? PipelineStepStatusValue.Success : PipelineStepStatusValue.Error;

        if (existing is null)
        {
            db.PipelineStatuses.Add(new PipelineStepStatus
            {
                Id = Guid.NewGuid(),
                MediaId = evt.MediaId,
                StepName = evt.StepName,
                Status = newStatus,
                CompletedAt = evt.CompletedAt
            });
        }
        else
        {
            existing.Status = newStatus;
            existing.CompletedAt = evt.CompletedAt;
        }

        await db.SaveChangesAsync(ct);

        logger.LogInformation(
            "[NATS:PipelineStatus] Status gespeichert: MediaId={MediaId}, Step={Step}, Status={Status}",
            evt.MediaId, evt.StepName, newStatus);
    }
}
