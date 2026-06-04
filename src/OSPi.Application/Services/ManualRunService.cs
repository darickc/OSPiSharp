using OSPi.Application.Engine;

namespace OSPi.Application.Services;

public sealed class ManualRunService : IManualRunService
{
    private readonly SprinklerEngine _engine;

    public ManualRunService(SprinklerEngine engine) => _engine = engine;

    public void TurnOn(int zoneId) => _engine.Post(new EngineCommand.SetZone(zoneId, true));

    public void TurnOff(int zoneId) => _engine.Post(new EngineCommand.SetZone(zoneId, false));

    public void Toggle(int zoneId, bool on) => _engine.Post(new EngineCommand.SetZone(zoneId, on));

    public void StopAll() => _engine.Post(new EngineCommand.StopAll());
}
