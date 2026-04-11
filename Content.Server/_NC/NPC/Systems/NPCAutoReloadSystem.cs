// Content.Server/_NC/NPC/Systems/NPCAutoReloadSystem.cs
// System that automatically reloads ballistic weapons for NPCs
// when their magazine runs empty, searching pockets for spare mags.

using Content.Shared._NC.NPC;
using Content.Shared._NC.Weapons.Ranged.NCWeapon;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction.Events;
using Content.Shared.Inventory;
using Content.Shared.Popups;
using Content.Shared.Weapons.Ranged;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Containers;

namespace Content.Server._NC.NPC.Systems;

/// <summary>
///     Periodically checks if an NPC's held gun is empty and attempts
///     to reload it from inventory (pockets). Works with magazine-fed weapons
///     (MagazineAmmoProvider / ChamberMagazineAmmoProvider).
/// </summary>
public sealed class NPCAutoReloadSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedGunSystem _gunSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popups = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;

    // Slot IDs used by ballistic weapons in SS14.
    private const string MagazineSlot = "gun_magazine";
    private const string ChamberSlot = "gun_chamber";

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NPCAutoReloadComponent, HandsComponent>();

        while (query.MoveNext(out var uid, out var reload, out var hands))
        {
            // 1. Get whatever the NPC is holding in its active hand.
            if (hands.ActiveHandEntity is not { } heldEntity)
                continue;

            // 2. Periodic Reload Check (Inventory search is expensive)
            reload.Accumulator += frameTime;
            if (reload.Accumulator < reload.CheckInterval)
                continue;

            reload.Accumulator = 0f;

            // 3. Check if the held item is a gun with a magazine slot.
            if (!HasComp<MagazineAmmoProviderComponent>(heldEntity) &&
                !HasComp<ChamberMagazineAmmoProviderComponent>(heldEntity))
                continue;

            // 4. Check current ammo count — if not empty, skip.
            var ammoEv = new GetAmmoCountEvent();
            RaiseLocalEvent(heldEntity, ref ammoEv);

            if (ammoEv.Count > 0)
                continue;

            // 5. Gun is empty. Try to eject the current (empty) magazine first.
            if (_container.TryGetContainer(heldEntity, MagazineSlot, out var magSlotContainer) &&
                magSlotContainer is ContainerSlot { ContainedEntity: not null })
            {
                _itemSlots.TryEject(heldEntity, MagazineSlot, uid, out _, excludeUserAudio: true);
            }

            // 6. Search the NPC's inventory for a spare magazine.
            var newMag = FindMagazineInInventory(uid, heldEntity);
            if (newMag == null)
                continue;

            // 7. Insert the new magazine.
            if (_itemSlots.TryInsert(heldEntity, MagazineSlot, newMag.Value, uid, excludeUserAudio: true))
            {
                break; 
            }
        }
    }

    /// <summary>
    ///     Checks if the weapon's chamber slot is empty.
    /// </summary>
    private bool IsChamberEmpty(EntityUid gun)
    {
        return _container.TryGetContainer(gun, ChamberSlot, out var container) &&
               container is ContainerSlot slot &&
               slot.ContainedEntity == null;
    }

    private EntityUid? FindMagazineInInventory(EntityUid npc, EntityUid gun)
    {
        if (!_itemSlots.TryGetSlot(gun, MagazineSlot, out var itemSlot))
            return null;

        var enumerator = _inventory.GetHandOrInventoryEntities(npc);

        foreach (var item in enumerator)
        {
            if (item == gun)
                continue;

            if (_itemSlots.CanInsert(gun, item, null, itemSlot))
                return item;
        }

        return null;
    }
}
