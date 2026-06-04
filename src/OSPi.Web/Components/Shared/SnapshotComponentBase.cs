using Microsoft.AspNetCore.Components;
using OSPi.Application.Engine;

namespace OSPi.Web.Components.Shared;

/// <summary>
/// Base for components that render the engine's live <see cref="StatusSnapshot"/>. Subscribes to
/// <see cref="IStateHub.SnapshotPublished"/> on init and re-renders on each push (delivered over
/// the Blazor Server circuit — no separate SignalR hub), unsubscribing on dispose.
/// </summary>
public abstract class SnapshotComponentBase : ComponentBase, IDisposable
{
    [Inject] protected IStateHub StateHub { get; set; } = default!;

    /// <summary>The most recent snapshot, or null before the engine's first publish.</summary>
    protected StatusSnapshot? Snapshot { get; private set; }

    protected override void OnInitialized()
    {
        Snapshot = StateHub.Latest;
        StateHub.SnapshotPublished += OnSnapshot;
    }

    private void OnSnapshot(StatusSnapshot snapshot)
    {
        Snapshot = snapshot;
        _ = InvokeAsync(StateHasChanged);
    }

    public void Dispose() => StateHub.SnapshotPublished -= OnSnapshot;
}
