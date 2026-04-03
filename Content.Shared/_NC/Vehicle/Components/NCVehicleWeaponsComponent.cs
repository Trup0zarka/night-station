using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Vehicle.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCVehicleWeaponsComponent : Component
{
    [AutoNetworkedField]
    public EntityUid? Operator;

    [AutoNetworkedField]
    public EntityUid? SelectedWeapon;

    [AutoNetworkedField]
    public Dictionary<EntityUid, EntityUid> OperatorSelections = new();

    [AutoNetworkedField]
    public Dictionary<EntityUid, EntityUid> HardpointOperators = new();

    [DataField, AutoNetworkedField]
    public bool StabilizationEnabled = true;

    [DataField, AutoNetworkedField]
    public bool AutoEnabled = false;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleWeaponsSeatComponent : Component
{
    [DataField]
    public bool IsPrimaryOperatorSeat = false;

    [DataField]
    public bool AllowUiSelection = true;

    [DataField]
    public bool AllowHotbarSelection = true;

    [DataField]
    public List<ProtoId<RMCHardpointSlotTypePrototype>> AllowedHardpointTypes = new();

    [DataField]
    public float BaseViewPvsScale = 1.5f;
}
