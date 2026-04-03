using System.Collections.Generic;
using System.Linq;
using Content.Shared._NC.Vehicle.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Ranged.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Network;

namespace Content.Shared._NC.Vehicle;

public sealed partial class VehicleSystem
{
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;

    private void InitializeWeapons()
    {
        SubscribeLocalEvent<VehicleWeaponsSeatComponent, StrappedEvent>(OnWeaponSeatStrapped);
        SubscribeLocalEvent<VehicleWeaponsSeatComponent, UnstrappedEvent>(OnWeaponSeatUnstrapped);

        SubscribeLocalEvent<VehicleWeaponsSeatComponent, RMCVehicleWeaponsSelectMessage>(OnWeaponsSelect);
        SubscribeLocalEvent<VehicleWeaponsSeatComponent, RMCVehicleWeaponsStabilizationMessage>(OnWeaponsStabilization);
        SubscribeLocalEvent<VehicleWeaponsSeatComponent, RMCVehicleWeaponsAutoModeMessage>(OnWeaponsAutoMode);
    }

    private void OnWeaponSeatStrapped(Entity<VehicleWeaponsSeatComponent> ent, ref StrappedEvent args)
    {
        if (_net.IsClient)
            return;

        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle == null)
            return;

        var vehicleUid = vehicle.Value;
        var weapons = EnsureComp<RMCVehicleWeaponsComponent>(vehicleUid);
        
        if (ent.Comp.IsPrimaryOperatorSeat)
            weapons.Operator = args.Buckle.Owner;

        var operatorComp = EnsureComp<VehicleWeaponsOperatorComponent>(args.Buckle.Owner);
        operatorComp.Vehicle = vehicle;
        Dirty(args.Buckle.Owner, operatorComp);

        _eye.SetTarget(args.Buckle.Owner, vehicleUid);
        _ui.OpenUi(ent.Owner, RMCVehicleWeaponsUiKey.Key, args.Buckle.Owner);
        UpdateWeaponsUiForAllOperators(vehicleUid, weapons);
    }

    private void OnWeaponSeatUnstrapped(Entity<VehicleWeaponsSeatComponent> ent, ref UnstrappedEvent args)
    {
        if (_net.IsClient)
            return;

        RemCompDeferred<VehicleWeaponsOperatorComponent>(args.Buckle.Owner);
        _ui.CloseUi(ent.Owner, RMCVehicleWeaponsUiKey.Key, args.Buckle.Owner);

        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle == null)
            return;

        var vehicleUid = vehicle.Value;
        if (TryComp(vehicleUid, out RMCVehicleWeaponsComponent? weapons))
        {
            if (weapons.Operator == args.Buckle.Owner)
                weapons.Operator = null;
            
            weapons.OperatorSelections.Remove(args.Buckle.Owner);
            UpdateWeaponsUiForAllOperators(vehicleUid, weapons);
        }

        if (TryComp(args.Buckle.Owner, out EyeComponent? eye) && eye.Target == vehicleUid)
            _eye.SetTarget(args.Buckle.Owner, null, eye);
    }

    private void OnWeaponsSelect(Entity<VehicleWeaponsSeatComponent> ent, ref RMCVehicleWeaponsSelectMessage args)
    {
        var user = args.Actor;

        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle == null)
            return;

        var vehicleUid = vehicle.Value;
        if (!TryComp(vehicleUid, out RMCVehicleWeaponsComponent? weapons))
            return;

        var weaponUid = GetEntity(args.MountedEntity);
        if (weaponUid == null)
        {
            weapons.OperatorSelections.Remove(user);
        }
        else
        {
            weapons.OperatorSelections[user] = weaponUid.Value;
            
            if (TryComp(user, out VehicleWeaponsOperatorComponent? op))
            {
                op.SelectedWeapon = weaponUid;
                Dirty(user, op);
            }
        }

        UpdateWeaponsUiForAllOperators(vehicleUid, weapons);
    }

    private void OnWeaponsStabilization(Entity<VehicleWeaponsSeatComponent> ent, ref RMCVehicleWeaponsStabilizationMessage args)
    {
        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle == null)
            return;

        if (TryComp(vehicle.Value, out RMCVehicleWeaponsComponent? weapons))
        {
            weapons.StabilizationEnabled = args.Enabled;
            Dirty(vehicle.Value, weapons);
            UpdateWeaponsUiForAllOperators(vehicle.Value, weapons);
        }
    }

    private void OnWeaponsAutoMode(Entity<VehicleWeaponsSeatComponent> ent, ref RMCVehicleWeaponsAutoModeMessage args)
    {
        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle == null)
            return;

        if (TryComp(vehicle.Value, out RMCVehicleWeaponsComponent? weapons))
        {
            weapons.AutoEnabled = args.Enabled;
            Dirty(vehicle.Value, weapons);
            UpdateWeaponsUiForAllOperators(vehicle.Value, weapons);
        }
    }

    private void UpdateWeaponsUiForAllOperators(EntityUid vehicle, RMCVehicleWeaponsComponent weapons)
    {
        var entries = BuildWeaponsUiEntries(vehicle, weapons);
        
        var query = EntityQueryEnumerator<VehicleWeaponsOperatorComponent, BuckleComponent>();
        while (query.MoveNext(out var user, out var op, out var buckle))
        {
            if (op.Vehicle != vehicle)
                continue;

            if (buckle.BuckledTo is not { } seat || !HasComp<VehicleWeaponsSeatComponent>(seat))
                continue;

            var state = new RMCVehicleWeaponsUiState(
                GetNetEntity(vehicle),
                entries,
                true, // canToggleStabilization
                weapons.StabilizationEnabled,
                true, // canToggleAuto
                weapons.AutoEnabled);

            _ui.SetUiState(seat, RMCVehicleWeaponsUiKey.Key, state);
        }
    }

    private List<RMCVehicleWeaponsUiEntry> BuildWeaponsUiEntries(EntityUid vehicle, RMCVehicleWeaponsComponent weapons)
    {
        var entries = new List<RMCVehicleWeaponsUiEntry>();
        
        if (!TryComp(vehicle, out RMCHardpointSlotsComponent? slots) ||
            !TryComp(vehicle, out ItemSlotsComponent? itemSlots))
        {
            return entries;
        }

        foreach (var slot in slots.Slots)
        {
            if (slot.SlotTypes.Contains("Turret") || slot.SlotTypes.Contains("Cannon"))
            {
                if (!_itemSlots.TryGetSlot(vehicle, slot.Id, out var itemSlot) || itemSlot.Item == null)
                    continue;

                var weapon = itemSlot.Item.Value;
                var hasIntegrity = TryComp(weapon, out RMCHardpointIntegrityComponent? integrity);
                var gun = CompOrNull<GunComponent>(weapon);

                var entry = new RMCVehicleWeaponsUiEntry(
                    slot.Id,
                    slot.SlotTypes.FirstOrDefault().ToString() ?? "Unknown",
                    GetNetEntity(weapon),
                    Name(weapon),
                    GetNetEntity(weapon),
                    true, // hasItem
                    true, // selectable
                    weapons.OperatorSelections.Values.Contains(weapon),
                    gun?.ShotCounter ?? 0,
                    100, // capacity placeholder
                    gun != null,
                    0, 0, 0, false, // magazine data placeholder
                    null, false, // operator data placeholder
                    integrity?.Integrity ?? 0,
                    integrity?.MaxIntegrity ?? 0,
                    hasIntegrity,
                    0, 0, false // cooldown placeholder
                );
                
                entries.Add(entry);
            }
        }

        return entries;
    }
}
