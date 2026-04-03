using Content.Shared._NC.Vehicle.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Tools.Systems;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._NC.Vehicle;

public sealed class NCVehicleHardpointSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly ItemSlotsSystem _itemSlots = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedToolSystem _tool = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<RMCHardpointSlotsComponent, ItemSlotInsertAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<RMCHardpointSlotsComponent, RMCHardpointInsertDoAfterEvent>(OnInsertDoAfter);
        SubscribeLocalEvent<RMCHardpointSlotsComponent, GetVerbsEvent<InteractionVerb>>(OnGetRemoveVerbs);
        SubscribeLocalEvent<RMCHardpointSlotsComponent, RMCHardpointRemoveDoAfterEvent>(OnHardpointRemoveDoAfter);
    }

    private void OnInsertAttempt(Entity<RMCHardpointSlotsComponent> ent, ref ItemSlotInsertAttemptEvent args)
    {
        if (args.User == null)
            return;

        if (!TryGetSlot(ent.Comp, args.Slot.ID, out var slot))
            return;

        if (ent.Comp.CompletingInserts.Contains(slot.Id))
            return;

        if (!IsValidHardpoint(args.Item, ent.Comp, slot))
        {
            args.Cancelled = true;
            return;
        }

        if (slot.InsertDelay <= 0f)
            return;

        if (ent.Comp.PendingInsertUsers.Contains(args.User.Value))
        {
            args.Cancelled = true;
            return;
        }

        if (!ent.Comp.PendingInserts.Add(slot.Id))
        {
            args.Cancelled = true;
            return;
        }

        args.Cancelled = true;
        ent.Comp.PendingInsertUsers.Add(args.User.Value);

        var doAfter = new DoAfterArgs(EntityManager, args.User.Value, slot.InsertDelay, new RMCHardpointInsertDoAfterEvent(slot.Id), ent.Owner, ent.Owner, args.Item)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnHandChange = true,
            BreakOnDropItem = true,
            BreakOnWeightlessMove = true,
            NeedHand = true,
            RequireCanInteract = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            ent.Comp.PendingInserts.Remove(slot.Id);
            ent.Comp.PendingInsertUsers.Remove(args.User.Value);
        }
    }

    private void OnInsertDoAfter(Entity<RMCHardpointSlotsComponent> ent, ref RMCHardpointInsertDoAfterEvent args)
    {
        ent.Comp.PendingInserts.Remove(args.SlotId);
        ent.Comp.PendingInsertUsers.Remove(args.User);

        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (args.Used is not { } item || string.IsNullOrEmpty(args.SlotId))
            return;

        if (!TryComp(ent.Owner, out ItemSlotsComponent? itemSlots))
            return;

        if (!TryGetSlot(ent.Comp, args.SlotId, out var hardpointSlot))
            return;

        if (!_itemSlots.TryGetSlot(ent.Owner, args.SlotId, out var slot, itemSlots))
            return;

        if (!IsValidHardpoint(item, ent.Comp, hardpointSlot))
            return;

        ent.Comp.CompletingInserts.Add(args.SlotId);
        _itemSlots.TryInsertFromHand(ent.Owner, slot, args.User, excludeUserAudio: false);
        ent.Comp.CompletingInserts.Remove(args.SlotId);
    }

    private void OnGetRemoveVerbs(Entity<RMCHardpointSlotsComponent> ent, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using == null)
            return;

        if (!_tool.HasQuality(args.Using.Value, ent.Comp.RemoveToolQuality))
            return;

        if (!TryComp(ent.Owner, out ItemSlotsComponent? itemSlots))
            return;

        foreach (var slot in ent.Comp.Slots)
        {
            if (!_itemSlots.TryGetSlot(ent.Owner, slot.Id, out var itemSlot, itemSlots) || !itemSlot.HasItem)
                continue;

            if (HasComp<RMCHardpointNoRemoveComponent>(itemSlot.Item!.Value))
                continue;

            var user = args.User;
            var slotId = slot.Id;
            var tool = args.Using.Value;
            var verb = new InteractionVerb
            {
                Act = () => TryStartHardpointRemoval(ent.Owner, ent.Comp, user, slotId, tool),
                Category = VerbCategory.Eject,
                Text = Loc.GetString("rmc-hardpoint-remove-verb", ("slot", Name(itemSlot.Item!.Value))),
                Priority = itemSlot.Priority,
                IconEntity = GetNetEntity(itemSlot.Item),
            };

            args.Verbs.Add(verb);
        }
    }

    private void OnHardpointRemoveDoAfter(Entity<RMCHardpointSlotsComponent> ent, ref RMCHardpointRemoveDoAfterEvent args)
    {
        ent.Comp.PendingRemovals.Remove(args.SlotId);

        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (!TryComp(ent.Owner, out ItemSlotsComponent? itemSlots))
            return;

        if (!_itemSlots.TryGetSlot(ent.Owner, args.SlotId, out var itemSlot, itemSlots) || !itemSlot.HasItem)
            return;

        _itemSlots.TryEjectToHands(ent.Owner, itemSlot, args.User, true);
    }

    private void TryStartHardpointRemoval(EntityUid uid, RMCHardpointSlotsComponent component, EntityUid user, string slotId, EntityUid tool)
    {
        if (!component.PendingRemovals.Add(slotId))
            return;

        if (!TryGetSlot(component, slotId, out var slot))
        {
            component.PendingRemovals.Remove(slotId);
            return;
        }

        var delay = slot.RemoveDelay > 0f ? slot.RemoveDelay : slot.InsertDelay;
        var doAfter = new DoAfterArgs(EntityManager, user, delay, new RMCHardpointRemoveDoAfterEvent(slotId), uid, uid, tool)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnHandChange = true,
            BreakOnDropItem = true,
            BreakOnWeightlessMove = true,
            NeedHand = true,
            RequireCanInteract = true,
            DuplicateCondition = DuplicateConditions.SameEvent,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            component.PendingRemovals.Remove(slotId);
        }
    }

    public bool IsValidHardpoint(EntityUid item, RMCHardpointSlotsComponent slots, RMCHardpointSlot slot)
    {
        if (!TryComp(item, out RMCHardpointItemComponent? itemComp))
            return false;

        if (!itemComp.VehicleFamilies.Contains(slots.VehicleFamily))
            return false;

        foreach (var slotType in slot.SlotTypes)
        {
            if (itemComp.SlotTypes.Contains(slotType))
                return true;
        }

        return false;
    }

    public bool TryGetSlot(RMCHardpointSlotsComponent component, string? slotId, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out RMCHardpointSlot? slot)
    {
        slot = null;
        if (string.IsNullOrEmpty(slotId))
            return false;

        foreach (var s in component.Slots)
        {
            if (s.Id == slotId)
            {
                slot = s;
                return true;
            }
        }

        return false;
    }
}
