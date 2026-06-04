using OSPi.Domain.Entities;

namespace OSPi.Application.Persistence;

/// <summary>Reads and updates the single controller-settings row.</summary>
public interface IControllerSettingsRepository
{
    Task<ControllerSettings> GetAsync(CancellationToken ct = default);

    Task UpdateAsync(ControllerSettings settings, CancellationToken ct = default);
}
