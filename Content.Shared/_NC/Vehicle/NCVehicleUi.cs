using System;
using System.Collections.Generic;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization;

namespace Content.Shared._NC.Vehicle;

// Consolidation of all vehicle-related UI keys and states from RMC-14 for the _NC project.

[Serializable, NetSerializable]
public enum RMCHardpointUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum RMCVehicleWeaponsUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public enum RMCVehicleAmmoLoaderUiKey : byte
{
    Key,
}

// --- Hardpoint UI ---

[Serializable, NetSerializable]
public sealed class RMCHardpointUiEntry
{
    public readonly string SlotId;
    public readonly string HardpointType;
    public readonly string? InstalledName;
    public readonly NetEntity? InstalledEntity;
    public readonly float Integrity;
    public readonly float MaxIntegrity;
    public readonly bool HasIntegrity;
    public readonly bool HasItem;
    public readonly bool Required;
    public readonly bool Removing;

    public RMCHardpointUiEntry(
        string slotId,
        string hardpointType,
        string? installedName,
        NetEntity? installedEntity,
        float integrity,
        float maxIntegrity,
        bool hasIntegrity,
        bool hasItem,
        bool required,
        bool removing)
    {
        SlotId = slotId;
        HardpointType = hardpointType;
        InstalledName = installedName;
        InstalledEntity = installedEntity;
        Integrity = integrity;
        MaxIntegrity = maxIntegrity;
        HasIntegrity = hasIntegrity;
        HasItem = hasItem;
        Required = required;
        Removing = removing;
    }
}

[Serializable, NetSerializable]
public sealed class RMCHardpointBoundUserInterfaceState : BoundUserInterfaceState
{
    public readonly List<RMCHardpointUiEntry> Hardpoints;
    public readonly float FrameIntegrity;
    public readonly float FrameMaxIntegrity;
    public readonly bool HasFrameIntegrity;
    public readonly string? Error;

    public RMCHardpointBoundUserInterfaceState(
        List<RMCHardpointUiEntry> hardpoints,
        float frameIntegrity,
        float frameMaxIntegrity,
        bool hasFrameIntegrity,
        string? error)
    {
        Hardpoints = hardpoints;
        FrameIntegrity = frameIntegrity;
        FrameMaxIntegrity = frameMaxIntegrity;
        HasFrameIntegrity = hasFrameIntegrity;
        Error = error;
    }
}

[Serializable, NetSerializable]
public sealed class RMCHardpointRemoveMessage : BoundUserInterfaceMessage
{
    public readonly string SlotId;

    public RMCHardpointRemoveMessage(string slotId)
    {
        SlotId = slotId;
    }
}

// --- Weapons UI ---

[Serializable, NetSerializable]
public sealed class RMCVehicleWeaponsUiEntry
{
    public readonly string SlotId;
    public readonly string HardpointType;
    public readonly NetEntity? MountedEntity;
    public readonly string? InstalledName;
    public readonly NetEntity? InstalledEntity;
    public readonly bool HasItem;
    public readonly bool Selectable;
    public readonly bool Selected;
    public readonly int AmmoCount;
    public readonly int AmmoCapacity;
    public readonly bool HasAmmo;
    public readonly int MagazineSize;
    public readonly int StoredMagazines;
    public readonly int MaxStoredMagazines;
    public readonly bool HasMagazineData;
    public readonly string? OperatorName;
    public readonly bool OperatorIsSelf;
    public readonly float Integrity;
    public readonly float MaxIntegrity;
    public readonly bool HasIntegrity;
    public readonly float CooldownRemaining;
    public readonly float CooldownTotal;
    public readonly bool IsOnCooldown;

    public RMCVehicleWeaponsUiEntry(
        string slotId,
        string hardpointType,
        NetEntity? mountedEntity,
        string? installedName,
        NetEntity? installedEntity,
        bool hasItem,
        bool selectable,
        bool selected,
        int ammoCount,
        int ammoCapacity,
        bool hasAmmo,
        int magazineSize,
        int storedMagazines,
        int maxStoredMagazines,
        bool hasMagazineData,
        string? operatorName,
        bool operatorIsSelf,
        float integrity,
        float maxIntegrity,
        bool hasIntegrity,
        float cooldownRemaining,
        float cooldownTotal,
        bool isOnCooldown)
    {
        SlotId = slotId;
        HardpointType = hardpointType;
        MountedEntity = mountedEntity;
        InstalledName = installedName;
        InstalledEntity = installedEntity;
        HasItem = hasItem;
        Selectable = selectable;
        Selected = selected;
        AmmoCount = ammoCount;
        AmmoCapacity = ammoCapacity;
        HasAmmo = hasAmmo;
        MagazineSize = magazineSize;
        StoredMagazines = storedMagazines;
        MaxStoredMagazines = maxStoredMagazines;
        HasMagazineData = hasMagazineData;
        OperatorName = operatorName;
        OperatorIsSelf = operatorIsSelf;
        Integrity = integrity;
        MaxIntegrity = maxIntegrity;
        HasIntegrity = hasIntegrity;
        CooldownRemaining = cooldownRemaining;
        CooldownTotal = cooldownTotal;
        IsOnCooldown = isOnCooldown;
    }
}

[Serializable, NetSerializable]
public sealed class RMCVehicleWeaponsUiState : BoundUserInterfaceState
{
    public readonly NetEntity Vehicle;
    public readonly List<RMCVehicleWeaponsUiEntry> Hardpoints;
    public readonly bool CanToggleStabilization;
    public readonly bool StabilizationEnabled;
    public readonly bool CanToggleAuto;
    public readonly bool AutoEnabled;

    public RMCVehicleWeaponsUiState(
        NetEntity vehicle,
        List<RMCVehicleWeaponsUiEntry> hardpoints,
        bool canToggleStabilization,
        bool stabilizationEnabled,
        bool canToggleAuto,
        bool autoEnabled)
    {
        Vehicle = vehicle;
        Hardpoints = hardpoints;
        CanToggleStabilization = canToggleStabilization;
        StabilizationEnabled = stabilizationEnabled;
        CanToggleAuto = canToggleAuto;
        AutoEnabled = autoEnabled;
    }
}

[Serializable, NetSerializable]
public sealed class RMCVehicleWeaponsSelectMessage : BoundUserInterfaceMessage
{
    public readonly NetEntity? MountedEntity;

    public RMCVehicleWeaponsSelectMessage(NetEntity? mountedEntity)
    {
        MountedEntity = mountedEntity;
    }
}

[Serializable, NetSerializable]
public sealed class RMCVehicleWeaponsStabilizationMessage : BoundUserInterfaceMessage
{
    public readonly bool Enabled;

    public RMCVehicleWeaponsStabilizationMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

[Serializable, NetSerializable]
public sealed class RMCVehicleWeaponsAutoModeMessage : BoundUserInterfaceMessage
{
    public readonly bool Enabled;

    public RMCVehicleWeaponsAutoModeMessage(bool enabled)
    {
        Enabled = enabled;
    }
}

[Serializable, NetSerializable]
public sealed class RMCVehicleWeaponsCooldownFeedbackMessage : BoundUserInterfaceMessage
{
    public readonly float RemainingSeconds;

    public RMCVehicleWeaponsCooldownFeedbackMessage(float remainingSeconds)
    {
        RemainingSeconds = remainingSeconds;
    }
}

// --- Ammo Loader UI ---

[Serializable, NetSerializable]
public sealed class RMCVehicleAmmoLoaderUiEntry
{
    public readonly string SlotId;
    public readonly string HardpointType;
    public readonly string? InstalledName;
    public readonly NetEntity? InstalledEntity;
    public readonly int ChamberedRounds;
    public readonly int MagazineSize;
    public readonly int StoredMagazines;
    public readonly int MaxStoredMagazines;
    public readonly bool CanLoad;

    public RMCVehicleAmmoLoaderUiEntry(
        string slotId,
        string hardpointType,
        string? installedName,
        NetEntity? installedEntity,
        int chamberedRounds,
        int magazineSize,
        int storedMagazines,
        int maxStoredMagazines,
        bool canLoad)
    {
        SlotId = slotId;
        HardpointType = hardpointType;
        InstalledName = installedName;
        InstalledEntity = installedEntity;
        ChamberedRounds = chamberedRounds;
        MagazineSize = magazineSize;
        StoredMagazines = storedMagazines;
        MaxStoredMagazines = maxStoredMagazines;
        CanLoad = canLoad;
    }
}

[Serializable, NetSerializable]
public sealed class RMCVehicleAmmoLoaderUiState : BoundUserInterfaceState
{
    public readonly List<RMCVehicleAmmoLoaderUiEntry> Hardpoints;
    public readonly int AmmoAmount;
    public readonly int AmmoMax;

    public RMCVehicleAmmoLoaderUiState(List<RMCVehicleAmmoLoaderUiEntry> hardpoints, int ammoAmount, int ammoMax)
    {
        Hardpoints = hardpoints;
        AmmoAmount = ammoAmount;
        AmmoMax = ammoMax;
    }
}

[Serializable, NetSerializable]
public sealed class RMCVehicleAmmoLoaderSelectMessage : BoundUserInterfaceMessage
{
    public readonly string SlotId;

    public RMCVehicleAmmoLoaderSelectMessage(string slotId)
    {
        SlotId = slotId;
    }
}
