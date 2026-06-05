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

    public void RunProgram(int programId) => _engine.Post(new EngineCommand.RunProgram(programId));

    public void RunZoneTimed(int hardwareBit, int seconds) =>
        _engine.Post(new EngineCommand.RunZoneTimed(hardwareBit, seconds));

    public void StopZone(int hardwareBit) => _engine.Post(new EngineCommand.CancelZone(hardwareBit));

    public void SetRainDelay(int minutes) => _engine.Post(new EngineCommand.SetRainDelay(minutes));

    public void Pause(int seconds) => _engine.Post(new EngineCommand.Pause(seconds));

    public void Resume() => _engine.Post(new EngineCommand.Resume());

    public void ReloadConfig() => _engine.Post(new EngineCommand.ReloadConfig());
}
