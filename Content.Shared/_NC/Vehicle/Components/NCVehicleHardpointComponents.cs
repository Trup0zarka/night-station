using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;
using Content.Shared.DoAfter;
using Content.Shared.Tools;

namespace Content.Shared._NC.Vehicle.Components;

[Prototype("rmcHardpointSlotType")]
public sealed partial class RMCHardpointSlotTypePrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;
}

[Prototype("rmcHardpointVehicleFamily")]
public sealed partial class RMCHardpointVehicleFamilyPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCHardpointComponent : Component
{
    [DataField, AutoNetworkedField]
    public ProtoId<RMCHardpointSlotTypePrototype> SlotType;

    [DataField, AutoNetworkedField]
    public EntProtoId? DefaultModule;

    [AutoNetworkedField]
    public EntityUid? Module;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class RMCHardpointNoRemoveComponent : Component { }

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCHardpointVisualComponent : Component
{
    [DataField, AutoNetworkedField]
    public string? Layer;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCVehicleArmorHardpointComponent : Component
{
    [DataField, AutoNetworkedField]
    public float FrontArmor;

    [DataField, AutoNetworkedField]
    public float SideArmor;

    [DataField, AutoNetworkedField]
    public float RearArmor;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCHardpointIntegrityComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Integrity = 100f;

    [DataField, AutoNetworkedField]
    public float MaxIntegrity = 100f;

    [DataField]
    public bool BypassEntryOnZero = false;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCHardpointSlotsComponent : Component
{
    [DataField, AutoNetworkedField]
    public List<RMCHardpointSlot> Slots = new();

    [DataField, AutoNetworkedField]
    public ProtoId<RMCHardpointVehicleFamilyPrototype> VehicleFamily;

    // Поправлено: ToolQualityPrototype обычно лежит в Content.Shared.Tools
    [DataField, AutoNetworkedField]
    public ProtoId<Content.Shared.Tools.ToolQualityPrototype> RemoveToolQuality = "Prying";

    [DataField]
    public HashSet<string> PendingInserts = new();

    [DataField]
    public HashSet<EntityUid> PendingInsertUsers = new();

    [DataField]
    public HashSet<string> CompletingInserts = new();

    [DataField]
    public HashSet<string> PendingRemovals = new();

    [DataField]
    public string? LastUiError;
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class RMCHardpointSlot
{
    [DataField(required: true)]
    public string Id = string.Empty;

    [DataField]
    public string? Name;

    [DataField]
    public List<ProtoId<RMCHardpointSlotTypePrototype>> SlotTypes = new();

    [DataField]
    public float InsertDelay = 2f;

    [DataField]
    public float RemoveDelay = 2f;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class RMCHardpointItemComponent : Component
{
    [DataField]
    public List<ProtoId<RMCHardpointSlotTypePrototype>> SlotTypes = new();

    [DataField]
    public List<ProtoId<RMCHardpointVehicleFamilyPrototype>> VehicleFamilies = new();
}

[Serializable, NetSerializable]
public sealed partial class RMCHardpointInsertDoAfterEvent : SimpleDoAfterEvent
{
    public string SlotId;
    public RMCHardpointInsertDoAfterEvent(string slotId) => SlotId = slotId;
}

[Serializable, NetSerializable]
public sealed partial class RMCHardpointRemoveDoAfterEvent : SimpleDoAfterEvent
{
    public string SlotId;
    public RMCHardpointRemoveDoAfterEvent(string slotId) => SlotId = slotId;
}

[Serializable, NetSerializable]
public enum RMCHardpointVisuals : byte
{
    ModuleState
}
