using Microsoft.AspNetCore.Components.Server.Circuits;

namespace LuminaVault.WebUI.Services;

/// <summary>
/// Scoped circuit handler that pauses the batch import when the owning browser
/// circuit disconnects (tab or window closed). Navigation within the same tab
/// keeps the circuit alive, so the import continues uninterrupted.
/// </summary>
public sealed class BatchImportCircuitHandler(BatchImportService batchImportService) : CircuitHandler
{
    public string? CircuitId { get; private set; }

    public override Task OnCircuitOpenedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        CircuitId = circuit.Id;
        return base.OnCircuitOpenedAsync(circuit, cancellationToken);
    }

    public override Task OnCircuitClosedAsync(Circuit circuit, CancellationToken cancellationToken)
    {
        if (batchImportService.OwnerCircuitId == circuit.Id)
            batchImportService.Pause();

        return base.OnCircuitClosedAsync(circuit, cancellationToken);
    }
}
