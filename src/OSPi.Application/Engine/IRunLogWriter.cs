using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OSPi.Application.Persistence;
using OSPi.Domain.Entities;

namespace OSPi.Application.Engine;

/// <summary>
/// Narrow seam the engine uses to persist a completed zone run. Production writes off-thread
/// through a scoped <see cref="IRunLogRepository"/>; tests substitute a synchronous recorder so
/// run-log assertions are deterministic on the tick thread.
/// </summary>
public interface IRunLogWriter
{
    /// <summary>Persist one completed run. All arguments are immutable scalars (thread-safe to capture).</summary>
    void Write(int zoneId, int? programId, DateTimeOffset start, DateTimeOffset end, int durationSeconds);
}

/// <summary>Discards run-log writes. Default when no writer is supplied (e.g. legacy test construction).</summary>
public sealed class NullRunLogWriter : IRunLogWriter
{
    public void Write(int zoneId, int? programId, DateTimeOffset start, DateTimeOffset end, int durationSeconds) { }
}

/// <summary>
/// Persists each completed run on a background task through a fresh DI scope, mirroring the
/// engine's existing off-thread scoped-write pattern (rain delay, single-run deletion). The
/// singleton engine cannot hold a scoped repository directly, so a scope is created per write.
/// A hard process shutdown may drop an in-flight write — acceptable for run history.
/// </summary>
public sealed class OffThreadRunLogWriter : IRunLogWriter
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OffThreadRunLogWriter> _logger;

    public OffThreadRunLogWriter(IServiceScopeFactory scopeFactory, ILogger<OffThreadRunLogWriter> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void Write(int zoneId, int? programId, DateTimeOffset start, DateTimeOffset end, int durationSeconds)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var repo = scope.ServiceProvider.GetRequiredService<IRunLogRepository>();
                await repo.AddAsync(new RunLogEntry
                {
                    ZoneId = zoneId,
                    ProgramId = programId,
                    StartTime = start,
                    EndTime = end,
                    DurationSeconds = durationSeconds,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to persist run-log entry for zone {ZoneId}.", zoneId);
            }
        });
    }
}
