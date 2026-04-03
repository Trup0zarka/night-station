using Robust.Shared.GameStates;
using Robust.Shared.Serialization;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Vehicle.Components;

[RegisterComponent, NetworkedComponent]
public sealed partial class RMCVehicleAmmoLoaderComponent : Component
{
    [DataField]
    public string? HardpointType;

    [DataField]
    public string? BulletType;

    [DataField]
    public float LoadDelay = 1.5f;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RMCVehicleHardpointAmmoComponent : Component
{
    [DataField, AutoNetworkedField]
    public int MagazineSize = 30;

    [DataField, AutoNetworkedField]
    public int StoredMagazines = 0;

    [DataField, AutoNetworkedField]
    public int MaxStoredMagazines = 5;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class BulletBoxComponent : Component
{
    [DataField]
    public string? BulletType;

    [DataField]
    public int Amount = 0;

    [DataField]
    public int Max = 1000;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class RefillableByBulletBoxComponent : Component
{
    [DataField]
    public string? BulletType;
}
