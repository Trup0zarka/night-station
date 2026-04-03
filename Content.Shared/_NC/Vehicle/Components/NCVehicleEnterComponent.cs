using System.Numerics;
using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._NC.Vehicle.Components;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class VehicleEntryPoint
{
    [DataField(required: true)]
    public Vector2 Offset;

    [DataField]
    public float Radius = 0.6f;

    [DataField]
    public Vector2? InteriorCoords;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleEnterComponent : Component
{
    [DataField(required: true)]
    public ResPath InteriorPath;

    [DataField]
    public int MaxPassengers = 0;

    [DataField]
    public List<VehicleEntryPoint> EntryPoints = new();

    [DataField]
    public float EnterDoAfter = 0f;

    [DataField]
    public float ExitDoAfter = 0f;

    [DataField]
    public Vector2 ExitOffset = Vector2.Zero;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleExitComponent : Component
{
    [AutoNetworkedField]
    public int EntryIndex;

    [NonSerialized]
    public bool PendingExit;
}

[Serializable, NetSerializable]
public sealed partial class VehicleEnterDoAfterEvent : SimpleDoAfterEvent
{
    [DataField(required: true)]
    public int EntryIndex;

    public override DoAfterEvent Clone()
    {
        return new VehicleEnterDoAfterEvent
        {
            EntryIndex = EntryIndex,
        };
    }
}

[Serializable, NetSerializable]
public sealed partial class VehicleExitDoAfterEvent : SimpleDoAfterEvent;

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleDriverSeatComponent : Component;
