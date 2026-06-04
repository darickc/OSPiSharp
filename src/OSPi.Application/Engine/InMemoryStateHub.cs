namespace OSPi.Application.Engine;

/// <summary>
/// Default <see cref="IStateHub"/>: keeps the latest snapshot in a volatile field for
/// lock-free reads and fans out the publish event. Register as a singleton.
/// </summary>
public sealed class InMemoryStateHub : IStateHub
{
    private volatile StatusSnapshot? _latest;

    public StatusSnapshot? Latest => _latest;

    public event Action<StatusSnapshot>? SnapshotPublished;

    public void Publish(StatusSnapshot snapshot)
    {
        _latest = snapshot;
        SnapshotPublished?.Invoke(snapshot);
    }
}
