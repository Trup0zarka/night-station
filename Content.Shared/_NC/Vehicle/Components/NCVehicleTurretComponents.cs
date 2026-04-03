using System.Numerics;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Vehicle.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleTurretComponent : Component
{
    [DataField, AutoNetworkedField]
    public float RotationSpeed = 90f;

    [DataField, AutoNetworkedField]
    public bool RotateToCursor = true;

    [DataField, AutoNetworkedField]
    public bool StabilizedRotation = false;

    [AutoNetworkedField]
    public Angle TargetRotation;

    [AutoNetworkedField]
    public Angle WorldRotation;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffset = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public bool UseDirectionalOffsets = false;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffsetNorth = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffsetEast = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffsetSouth = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public Vector2 PixelOffsetWest = Vector2.Zero;

    [DataField, AutoNetworkedField]
    public bool OffsetRotatesWithTurret = true;

    [DataField, AutoNetworkedField]
    public bool ShowOverlay = true;

    [AutoNetworkedField]
    public EntityUid? VisualEntity;

    [DataField, AutoNetworkedField]
    public float FireWhileRotatingGraceDegrees = 5f;

    [DataField, AutoNetworkedField]
    public bool UseBarrelDirectionForShots = true;

    [DataField, AutoNetworkedField]
    public float MaxShotCurvatureDegrees = 0f;

    [DataField, AutoNetworkedField]
    public float RotationInputDeadzoneDegrees = 1f;

    [DataField, AutoNetworkedField]
    public float ReverseDirectionDelay = 0.1f;

    [NonSerialized]
    public Angle? PendingTargetRotation;

    [NonSerialized]
    public TimeSpan PendingTargetApplyAt;

    [NonSerialized]
    public int PendingDirectionSign;

    [NonSerialized]
    public int LastAppliedDirectionSign;
}

[RegisterComponent, NetworkedComponent]
public sealed partial class VehicleTurretAttachmentComponent : Component { }

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleWeaponsOperatorComponent : Component
{
    [AutoNetworkedField]
    public EntityUid? Vehicle;

    [AutoNetworkedField]
    public EntityUid? SelectedWeapon;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VehicleTurretVisualComponent : Component
{
    [AutoNetworkedField]
    public NetEntity Turret;
}
