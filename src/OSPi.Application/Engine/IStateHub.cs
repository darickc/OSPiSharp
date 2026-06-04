namespace OSPi.Application.Engine;

/// <summary>
/// Holds the latest <see cref="StatusSnapshot"/> for lock-free reads and raises an event
/// when the engine publishes a new one. Defined in Application so the engine has no
/// dependency on SignalR; the Web project provides the implementation that bridges to
/// <c>IHubContext</c>.
/// </summary>
public interface IStateHub
{
    /// <summary>Most recently published snapshot, or null before the first tick.</summary>
    StatusSnapshot? Latest { get; }

    /// <summary>Raised each time the engine publishes a new snapshot.</summary>
    event Action<StatusSnapshot>? SnapshotPublished;

    /// <summary>Called by the engine to publish a new snapshot.</summary>
    void Publish(StatusSnapshot snapshot);
}
