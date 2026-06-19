
/// cho za govno


using Content.Shared.Materials;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype;

namespace Content.Shared._NC.Incubation
{
[RegisterComponent]
public sealed partial class IncubatorComponent : Component
{

[ViewVariables]
public ContainerSlot BodyContainer = default!;


    [ViewVariables]
    public IncubatorStatus Status = IncubatorStatus.Idle;

    [DataField("spawnMobPrototype", customTypeSerializer: typeof(PrototypeIdSerializer<EntityPrototype>))]
    public string SpawnMobPrototype = "MobHuman";

    [DataField("requiredBiomass")]
    public int RequiredBiomass = 100;

    [DataField("biomassMaterial", customTypeSerializer: typeof(PrototypeIdSerializer<MaterialPrototype>))]
    public string BiomassMaterial = "Biomass";

    [DataField("incubationTime")]
    public float IncubationTime = 30f;

    [ViewVariables]
    public float RemainingTime = 0f;

    [DataField("autoEject")]
    public bool AutoEject = false;

    [DataField("ghostRoleChance")]
    public float GhostRoleChance = 0.20f;
}

public enum IncubatorStatus : byte
{
    Idle,       // Ожидание
    Growing,    // выращивания тела
    Finished,   // Выращивание завершено
    NoPower     // Нет энергии
}


}