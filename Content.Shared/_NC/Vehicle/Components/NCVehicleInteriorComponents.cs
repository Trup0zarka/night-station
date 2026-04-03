using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Shared._NC.Vehicle.Components;

[RegisterComponent]
public sealed partial class NCVehicleInteriorComponent : Component
{
    public EntityUid Map = EntityUid.Invalid;
    public MapId MapId = MapId.Nullspace;
    public EntityCoordinates Entry;
    public EntityUid EntryParent = EntityUid.Invalid;
    public EntityUid Grid = EntityUid.Invalid;
    public HashSet<int> EntryLocks = new();
    public HashSet<EntityUid> Passengers = new();
}

[RegisterComponent]
public sealed partial class NCVehicleInteriorLinkComponent : Component
{
    public EntityUid Vehicle = EntityUid.Invalid;
}

[RegisterComponent]
public sealed partial class NCVehicleInteriorOccupantComponent : Component
{
    public EntityUid Vehicle = EntityUid.Invalid;
}
