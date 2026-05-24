using Content.Shared._NC.CitiNet;
using Content.Shared._NC.CitiNet.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Containers.ItemSlots;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._NC.CitiNet.Systems;

public sealed class NetBrowserSystem : SharedNetBrowserSystem
{
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly AccessReaderSystem _accessReader = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<NetBrowserComponent, BoundUIOpenedEvent>(OnUIOpened);
        SubscribeLocalEvent<NetBrowserComponent, EntInsertedIntoContainerMessage>(OnItemInserted);
        SubscribeLocalEvent<NetBrowserComponent, EntRemovedFromContainerMessage>(OnItemRemoved);
        SubscribeLocalEvent<NetBrowserComponent, NetBrowserNavigateMessage>(OnNavigateMessage);
    }

    private void OnNavigateMessage(EntityUid uid, NetBrowserComponent component, NetBrowserNavigateMessage args)
    {
        var user = args.Actor;
        if (user == default) return;

        // Check if the target site is restricted and if the user has access
        foreach (var site in PrototypeManager.EnumeratePrototypes<NetSitePrototype>())
        {
            if (site.URL == args.Url)
            {
                if (site.RequiredAccess.Count > 0 && 
                    !component.UnlockedUrls.Contains(site.URL) && 
                    !HasAccess(uid, user, site))
                {
                    // Access denied
                    return;
                }
                break;
            }
        }

        NavigateTo(uid, component, args.Url);
    }

    private void OnUIOpened(EntityUid uid, NetBrowserComponent component, BoundUIOpenedEvent args)
    {
        UpdateUserInterface(uid, component, args.Actor);
    }

    private void OnItemInserted(EntityUid uid, NetBrowserComponent component, EntInsertedIntoContainerMessage args)
    {
        // If a data chip or ID card is inserted, update the UI for all connected users
        UpdateAllUserInterfaces(uid, component);
        
        // Check if it's a data chip and unlock the site
        if (TryComp<DataChipComponent>(args.Entity, out var chip) && chip.UnlockedSiteId != null)
        {
            if (PrototypeManager.TryIndex<NetSitePrototype>(chip.UnlockedSiteId, out var site))
            {
                if (component.UnlockedUrls.Add(site.URL))
                {
                    Dirty(uid, component);
                    UpdateAllUserInterfaces(uid, component);
                }
            }
        }
    }

    private void OnItemRemoved(EntityUid uid, NetBrowserComponent component, EntRemovedFromContainerMessage args)
    {
        UpdateAllUserInterfaces(uid, component);
    }

    private void UpdateAllUserInterfaces(EntityUid uid, NetBrowserComponent component)
    {
        foreach (var actor in _uiSystem.GetActors(uid, NetBrowserUiKey.Key))
        {
            UpdateUserInterface(uid, component, actor);
        }
    }

    private void UpdateUserInterface(EntityUid uid, NetBrowserComponent component, EntityUid? user)
    {
        var availableSites = GetAvailableSites(uid, component, user);
        var state = new NetBrowserUiState(component.CurrentUrl, availableSites);
        _uiSystem.SetUiState(uid, NetBrowserUiKey.Key, state);
    }

    private List<string> GetAvailableSites(EntityUid uid, NetBrowserComponent component, EntityUid? user)
    {
        var result = new List<string>();
        var sites = PrototypeManager.EnumeratePrototypes<NetSitePrototype>();

        foreach (var site in sites)
        {
            if (component.UnlockedUrls.Contains(site.URL))
            {
                result.Add(site.ID);
                continue;
            }

            if (user != null && HasAccess(uid, user.Value, site))
            {
                result.Add(site.ID);
            }
            else if (site.RequiredAccess.Count == 0)
            {
                result.Add(site.ID);
            }
        }

        return result;
    }

    private bool HasAccess(EntityUid uid, EntityUid user, NetSitePrototype site)
    {
        if (site.RequiredAccess.Count == 0)
            return true;

        var accessSources = _accessReader.FindPotentialAccessItems(user);
        
        if (TryComp<ItemSlotsComponent>(uid, out var itemSlots))
        {
            foreach (var slot in itemSlots.Slots.Values)
            {
                if (slot.Item is { } item)
                    accessSources.Add(item);
            }
        }

        var accessTags = _accessReader.FindAccessTags(user, accessSources);

        foreach (var required in site.RequiredAccess)
        {
            if (!accessTags.Contains(required))
                return false;
        }

        return true;
    }

    public override void NavigateTo(EntityUid uid, NetBrowserComponent component, string url)
    {
        base.NavigateTo(uid, component, url);
        UpdateAllUserInterfaces(uid, component);
    }
}
