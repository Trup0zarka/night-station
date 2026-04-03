using System;
using System.Collections.Generic;
using Content.Shared._NC.Vehicle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.UserInterface;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Network;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._NC.Vehicle;

public sealed class NCVehicleAmmoLoaderSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedGunSystem _gun = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedUserInterfaceSystem _ui = default!;
    [Dependency] private readonly VehicleSystem _vehicleSystem = default!;

    private readonly Dictionary<EntityUid, Dictionary<EntityUid, EntityUid>> _activeAmmoBoxes = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RMCVehicleAmmoLoaderComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<RMCVehicleAmmoLoaderComponent, VehicleAmmoLoaderDoAfterEvent>(OnLoadDoAfter);
        SubscribeLocalEvent<RMCVehicleAmmoLoaderComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<RMCVehicleAmmoLoaderComponent, BoundUIClosedEvent>(OnUiClosed);
        SubscribeLocalEvent<RMCVehicleAmmoLoaderComponent, RMCVehicleAmmoLoaderSelectMessage>(OnUiSelect);
    }

    private void OnInteractUsing(Entity<RMCVehicleAmmoLoaderComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || _net.IsClient)
            return;

        if (!TryComp(args.Used, out BulletBoxComponent? box))
            return;

        if (!_vehicleSystem.TryGetVehicleFromInterior(ent.Owner, out var vehicleUid) || vehicleUid == null)
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-ammo-loader-no-vehicle"), ent, args.User);
            return;
        }

        if (box.Amount <= 0)
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-ammo-loader-empty", ("box", args.Used)), ent, args.User);
            return;
        }

        _ui.OpenUi(ent.Owner, RMCVehicleAmmoLoaderUiKey.Key, args.User);
        _activeAmmoBoxes.GetOrNew(ent.Owner)[args.User] = args.Used;
        UpdateUi(ent.Owner, box);
        args.Handled = true;
    }

    private void OnLoadDoAfter(Entity<RMCVehicleAmmoLoaderComponent> ent, ref VehicleAmmoLoaderDoAfterEvent args)
    {
        if (_net.IsClient || args.Cancelled || args.Handled || args.Used is not { } used)
            return;

        if (!TryComp(used, out BulletBoxComponent? box))
            return;

        if (!CanLoad(ent, args.User, box, args.SlotId, out _, out var ammoUid, out var ammo, out var hardpointAmmo))
            return;

        var magazineSize = Math.Max(1, hardpointAmmo!.MagazineSize);
        if (box.Amount < magazineSize)
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-ammo-loader-not-enough"), ent, args.User);
            return;
        }

        box.Amount -= magazineSize;
        Dirty(used, box);

        if (ammo!.Count == 0)
        {
            _gun.SetBallisticUnspawned((ammoUid, ammo), Math.Min(magazineSize, ammo.Capacity));
        }
        else
        {
            hardpointAmmo.StoredMagazines++;
            Dirty(ammoUid, hardpointAmmo);
        }

        _popup.PopupEntity(Loc.GetString("rmc-vehicle-ammo-loader-loaded", ("amount", magazineSize), ("target", ammoUid)), ent, args.User);
        UpdateUi(ent.Owner, box);
        args.Handled = true;
    }

    private void OnUiOpened(Entity<RMCVehicleAmmoLoaderComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (!args.UiKey.Equals(RMCVehicleAmmoLoaderUiKey.Key))
            return;

        if (_activeAmmoBoxes.TryGetValue(ent.Owner, out var users) && 
            users.TryGetValue(args.Actor, out var boxUid) &&
            TryComp(boxUid, out BulletBoxComponent? box))
        {
            UpdateUi(ent.Owner, box);
        }
    }

    private void OnUiClosed(Entity<RMCVehicleAmmoLoaderComponent> ent, ref BoundUIClosedEvent args)
    {
        if (!args.UiKey.Equals(RMCVehicleAmmoLoaderUiKey.Key))
            return;

        if (_activeAmmoBoxes.TryGetValue(ent.Owner, out var users))
        {
            users.Remove(args.Actor);
            if (users.Count == 0)
                _activeAmmoBoxes.Remove(ent.Owner);
        }
    }

    private void OnUiSelect(Entity<RMCVehicleAmmoLoaderComponent> ent, ref RMCVehicleAmmoLoaderSelectMessage args)
    {
        var user = args.Actor;

        if (!_hands.TryGetActiveItem(user, out var activeItem))
            return;

        if (!TryComp(activeItem, out BulletBoxComponent? box))
            return;

        var doAfter = new DoAfterArgs(EntityManager, user, TimeSpan.FromSeconds(ent.Comp.LoadDelay), new VehicleAmmoLoaderDoAfterEvent(args.SlotId), ent.Owner, ent.Owner, activeItem)
        {
            BreakOnMove = true,
            NeedHand = true,
        };

        _doAfter.TryStartDoAfter(doAfter);
    }

    private void UpdateUi(EntityUid loader, BulletBoxComponent box)
    {
        if (_net.IsClient)
            return;

        if (!_vehicleSystem.TryGetVehicleFromInterior(loader, out var vehicleUid) || vehicleUid == null)
            return;

        if (!TryComp(vehicleUid.Value, out RMCHardpointSlotsComponent? hardpoints) ||
            !TryComp(vehicleUid.Value, out ItemSlotsComponent? itemSlots))
        {
            return;
        }

        var entries = new List<RMCVehicleAmmoLoaderUiEntry>();

        foreach (var slot in hardpoints.Slots)
        {
            if (!_itemSlots.TryGetSlot(vehicleUid.Value, slot.Id, out var itemSlot) || itemSlot.Item == null)
                continue;

            var item = itemSlot.Item.Value;
            if (!TryComp(item, out BallisticAmmoProviderComponent? ammoProvider) ||
                !TryComp(item, out RMCVehicleHardpointAmmoComponent? hardpointAmmo))
                continue;

            var canLoad = box.Amount >= hardpointAmmo.MagazineSize && 
                          (ammoProvider.Count == 0 || hardpointAmmo.StoredMagazines < hardpointAmmo.MaxStoredMagazines);

            entries.Add(new RMCVehicleAmmoLoaderUiEntry(
                slot.Id,
                slot.Id, // Type placeholder
                Name(item),
                GetNetEntity(item),
                ammoProvider.Count,
                hardpointAmmo.MagazineSize,
                hardpointAmmo.StoredMagazines,
                hardpointAmmo.MaxStoredMagazines,
                canLoad));
        }

        _ui.SetUiState(loader, RMCVehicleAmmoLoaderUiKey.Key, new RMCVehicleAmmoLoaderUiState(entries, box.Amount, box.Max));
    }

    private bool CanLoad(
        Entity<RMCVehicleAmmoLoaderComponent> loader,
        EntityUid user,
        BulletBoxComponent box,
        string slotId,
        out EntityUid vehicle,
        out EntityUid ammoUid,
        out BallisticAmmoProviderComponent? ammo,
        out RMCVehicleHardpointAmmoComponent? hardpointAmmo)
    {
        ammoUid = default;
        ammo = default;
        hardpointAmmo = default;
        vehicle = default;

        if (!_vehicleSystem.TryGetVehicleFromInterior(loader.Owner, out var vehicleUid) || vehicleUid == null)
            return false;

        vehicle = vehicleUid.Value;

        if (!_itemSlots.TryGetSlot(vehicle, slotId, out var itemSlot) || itemSlot.Item == null)
            return false;

        ammoUid = itemSlot.Item.Value;
        if (!TryComp(ammoUid, out ammo) || !TryComp(ammoUid, out hardpointAmmo))
            return false;

        return true;
    }
}

[Serializable, NetSerializable]
public sealed partial class VehicleAmmoLoaderDoAfterEvent : DoAfterEvent
{
    [DataField(required: true)]
    public string SlotId = string.Empty;

    public VehicleAmmoLoaderDoAfterEvent() { }
    public VehicleAmmoLoaderDoAfterEvent(string slotId) => SlotId = slotId;

    public override DoAfterEvent Clone() => new VehicleAmmoLoaderDoAfterEvent(SlotId);
}
