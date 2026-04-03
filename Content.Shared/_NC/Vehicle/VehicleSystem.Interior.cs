using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared._NC.Vehicle.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Utility;

namespace Content.Shared._NC.Vehicle;

public sealed partial class VehicleSystem
{
    private void InitializeInterior()
    {
        SubscribeLocalEvent<VehicleEnterComponent, ActivateInWorldEvent>(OnVehicleEnterActivate);
        SubscribeLocalEvent<VehicleExitComponent, ActivateInWorldEvent>(OnVehicleExitActivate);
        SubscribeLocalEvent<VehicleEnterComponent, VehicleEnterDoAfterEvent>(OnVehicleEnterDoAfter);
        SubscribeLocalEvent<VehicleExitComponent, VehicleExitDoAfterEvent>(OnVehicleExitDoAfter);

        SubscribeLocalEvent<NCVehicleInteriorOccupantComponent, ComponentStartup>(OnOccupantStartup);
        SubscribeLocalEvent<NCVehicleInteriorOccupantComponent, ComponentRemove>(OnOccupantRemove);
        SubscribeLocalEvent<NCVehicleInteriorOccupantComponent, MapUidChangedEvent>(OnOccupantMapChanged);
    }

    private void OnVehicleEnterActivate(Entity<VehicleEnterComponent> ent, ref ActivateInWorldEvent args)
    {
        if (_net.IsClient)
            return;

        if (args.Handled)
            return;

        if (!TryFindEntry(ent, args.User, out var entryIndex))
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-use-doorway"), args.User, args.User);
            return;
        }

        var interior = EnsureComp<NCVehicleInteriorComponent>(ent.Owner);

        if (!interior.EntryLocks.Add(entryIndex))
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-busy"), args.User, args.User);
            return;
        }

        var doAfter = new DoAfterArgs(EntityManager, args.User, ent.Comp.EnterDoAfter, new VehicleEnterDoAfterEvent { EntryIndex = entryIndex }, ent.Owner)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = false,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            interior.EntryLocks.Remove(entryIndex);
            return;
        }

        args.Handled = true;
    }

    private bool TryEnter(Entity<VehicleEnterComponent> ent, EntityUid user, int entryIndex = -1)
    {
        if (!EnsureInterior(ent, out var interior))
            return false;

        PruneTrackedOccupants(ent.Owner, interior);

        if (ent.Comp.MaxPassengers > 0 && !interior.Passengers.Contains(user) && interior.Passengers.Count >= ent.Comp.MaxPassengers)
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-enter-passenger-full"), user, user);
            return false;
        }

        var coords = interior.Entry;
        if (entryIndex >= 0 && entryIndex < ent.Comp.EntryPoints.Count)
        {
            var entryPoint = ent.Comp.EntryPoints[entryIndex];
            if (entryPoint.InteriorCoords is { } interiorCoord)
            {
                var parent = interior.Grid.IsValid() ? interior.Grid : interior.EntryParent;
                var entityCoords = new EntityCoordinates(parent, interiorCoord);
                _transform.SetCoordinates(user, entityCoords);
                TrackOccupant(user, ent.Owner);
                return true;
            }
        }

        _transform.SetCoordinates(user, coords);
        TrackOccupant(user, ent.Owner);
        return true;
    }

    private bool EnsureInterior(Entity<VehicleEnterComponent> ent, [NotNullWhen(true)] out NCVehicleInteriorComponent? interior)
    {
        if (TryComp(ent.Owner, out interior) &&
            interior.MapId != MapId.Nullspace &&
            _mapManager.MapExists(interior.MapId))
        {
            return true;
        }

        interior = null;
        if (_net.IsClient)
            return false;

        interior = EnsureComp<NCVehicleInteriorComponent>(ent.Owner);

        var deserializeOptions = new DeserializationOptions
        {
            InitializeMaps = true,
        };

        if (!_mapLoader.TryLoadMap(ent.Comp.InteriorPath, out var loadedMap, out _, deserializeOptions))
        {
            Log.Error($"[VehicleEnter] Failed to load interior for {ToPrettyString(ent.Owner)} at {ent.Comp.InteriorPath}");
            return false;
        }

        if (loadedMap is not { } map)
            return false;

        var mapId = map.Comp.MapId;
        var mapUid = map.Owner;

        EntityUid entryParent = map.Owner;
        EntityUid interiorGrid = EntityUid.Invalid;
        
        var gridEnum = EntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridEnum.MoveNext(out var gridUid, out _, out var gridXform))
        {
            if (gridXform.MapID != mapId)
                continue;

            entryParent = gridUid;
            interiorGrid = gridUid;
            break;
        }

        var entryCoords = new EntityCoordinates(entryParent, Vector2.Zero);

        var exitQuery = EntityQueryEnumerator<VehicleExitComponent, TransformComponent>();
        while (exitQuery.MoveNext(out _, out _, out var xform))
        {
            if (xform.MapID != mapId)
                continue;

            entryCoords = xform.Coordinates;
            entryParent = xform.ParentUid.IsValid() ? xform.ParentUid : entryParent;
            break;
        }

        interior.Map = mapUid;
        interior.MapId = mapId;
        interior.Entry = entryCoords;
        interior.EntryParent = entryParent;
        interior.Grid = interiorGrid;
        interior.Passengers.Clear();

        var link = EnsureComp<NCVehicleInteriorLinkComponent>(mapUid);
        link.Vehicle = ent.Owner;

        return true;
    }

    private void CleanupInterior(EntityUid vehicle)
    {
        if (!TryComp(vehicle, out NCVehicleInteriorComponent? interior))
            return;

        foreach (var passenger in new List<EntityUid>(interior.Passengers))
        {
            if (TryComp(passenger, out NCVehicleInteriorOccupantComponent? occupant) &&
                occupant.Vehicle == vehicle)
            {
                RemComp<NCVehicleInteriorOccupantComponent>(passenger);
            }
        }

        if (interior.Map.IsValid() &&
            EntityManager.EntityExists(interior.Map) &&
            TryComp(interior.Map, out NCVehicleInteriorLinkComponent? link) &&
            link.Vehicle == vehicle)
        {
            RemComp<NCVehicleInteriorLinkComponent>(interior.Map);
        }

        RemComp<NCVehicleInteriorComponent>(vehicle);

        if (_net.IsClient)
            return;

        if (interior.MapId != MapId.Nullspace && _mapManager.MapExists(interior.MapId))
        {
            _mapManager.DeleteMap(interior.MapId);
        }
        else if (interior.Map.IsValid() && EntityManager.EntityExists(interior.Map))
        {
            Del(interior.Map);
        }
    }

    private void OnVehicleExitActivate(Entity<VehicleExitComponent> ent, ref ActivateInWorldEvent args)
    {
        if (_net.IsClient)
            return;

        if (args.Handled)
            return;

        if (ent.Comp.PendingExit)
        {
            _popup.PopupEntity(Loc.GetString("rmc-vehicle-exit-busy"), args.User, args.User);
            return;
        }

        if (!TryComp(ent, out TransformComponent? exitXform) || exitXform.MapID == MapId.Nullspace)
            return;

        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle is not { } vehicleUid)
            return;

        if (!TryComp(vehicleUid, out VehicleEnterComponent? enter))
            return;

        ent.Comp.PendingExit = true;

        var doAfter = new DoAfterArgs(EntityManager, args.User, enter.ExitDoAfter, new VehicleExitDoAfterEvent(), ent.Owner)
        {
            BreakOnMove = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
        {
            ent.Comp.PendingExit = false;
            return;
        }

        args.Handled = true;
    }

    private bool TryFindEntry(Entity<VehicleEnterComponent> ent, EntityUid user, out int entryIndex)
    {
        entryIndex = -1;

        if (ent.Comp.EntryPoints.Count == 0)
            return true;

        var vehicleXform = Transform(ent.Owner);
        var userXform = Transform(user);

        if (vehicleXform.MapID != userXform.MapID || vehicleXform.MapID == MapId.Nullspace)
            return false;

        var vehiclePos = _transform.GetWorldPosition(vehicleXform);
        var userPos = _transform.GetWorldPosition(userXform);
        var delta = userPos - vehiclePos;
        var localDelta = (-vehicleXform.LocalRotation).RotateVec(delta);

        for (var i = 0; i < ent.Comp.EntryPoints.Count; i++)
        {
            var entry = ent.Comp.EntryPoints[i];
            if ((localDelta - entry.Offset).Length() <= entry.Radius)
            {
                entryIndex = i;
                return true;
            }
        }

        return false;
    }

    private void OnVehicleEnterDoAfter(Entity<VehicleEnterComponent> ent, ref VehicleEnterDoAfterEvent args)
    {
        if (TryComp(ent.Owner, out NCVehicleInteriorComponent? interior))
            interior.EntryLocks.Remove(args.EntryIndex);

        if (args.Cancelled || args.Handled)
            return;

        args.Handled = TryEnter(ent, args.User, args.EntryIndex);
    }

    private bool TryExit(Entity<VehicleExitComponent> ent, EntityUid user)
    {
        if (!TryComp(ent, out TransformComponent? exitXform) || exitXform.MapID == MapId.Nullspace)
            return false;

        if (!TryGetVehicleFromInterior(ent.Owner, out var vehicle) || vehicle is not { } vehicleUid)
            return false;

        if (!TryComp(vehicleUid, out VehicleEnterComponent? enter))
            return false;

        var vehicleXform = Transform(vehicleUid);

        EntityUid? parent = vehicleXform.ParentUid;
        if (parent == null || !parent.Value.IsValid())
            parent = vehicleXform.MapUid;
        if (parent == null || !parent.Value.IsValid())
            return false;

        Vector2 offset;

        var entryIndex = ent.Comp.EntryIndex;
        if (entryIndex >= 0 && entryIndex < enter.EntryPoints.Count)
        {
            offset = enter.EntryPoints[entryIndex].Offset;
        }
        else
        {
            offset = enter.ExitOffset;
        }

        var rotated = vehicleXform.LocalRotation.RotateVec(offset);
        var position = vehicleXform.LocalPosition + rotated;

        var exitCoords = new EntityCoordinates(parent.Value, position);
        _transform.SetCoordinates(user, exitCoords);
        UntrackOccupant(user, vehicleUid);
        return true;
    }

    private void OnVehicleExitDoAfter(Entity<VehicleExitComponent> ent, ref VehicleExitDoAfterEvent args)
    {
        ent.Comp.PendingExit = false;

        if (args.Cancelled || args.Handled)
            return;

        args.Handled = TryExit(ent, args.User);
    }

    private void OnOccupantStartup(Entity<NCVehicleInteriorOccupantComponent> ent, ref ComponentStartup args)
    {
        _meta.AddFlag(ent, MetaDataFlags.ExtraTransformEvents);
    }

    private void OnOccupantRemove(Entity<NCVehicleInteriorOccupantComponent> ent, ref ComponentRemove args)
    {
        _meta.RemoveFlag(ent, MetaDataFlags.ExtraTransformEvents);

        if (ent.Comp.Vehicle.IsValid())
            UnregisterTrackedOccupant(ent.Comp.Vehicle, ent.Owner);
    }

    private void OnOccupantMapChanged(Entity<NCVehicleInteriorOccupantComponent> ent, ref MapUidChangedEvent args)
    {
        if (ent.Comp.Vehicle == EntityUid.Invalid)
            return;

        if (TryComp(ent.Comp.Vehicle, out NCVehicleInteriorComponent? interior) &&
            args.NewMapId == interior.MapId)
        {
            RegisterTrackedOccupant(ent.Comp.Vehicle, ent.Owner, interior);
            return;
        }

        RemCompDeferred<NCVehicleInteriorOccupantComponent>(ent.Owner);
    }

    private void TrackOccupant(EntityUid user, EntityUid vehicle)
    {
        var occupant = EnsureComp<NCVehicleInteriorOccupantComponent>(user);
        if (occupant.Vehicle.IsValid() &&
            occupant.Vehicle != vehicle)
        {
            UnregisterTrackedOccupant(occupant.Vehicle, user);
        }

        occupant.Vehicle = vehicle;

        if (TryComp(vehicle, out NCVehicleInteriorComponent? interior))
            RegisterTrackedOccupant(vehicle, user, interior);
    }

    private void UntrackOccupant(EntityUid user, EntityUid vehicle)
    {
        if (TryComp(user, out NCVehicleInteriorOccupantComponent? occupant) &&
            occupant.Vehicle == vehicle)
        {
            RemCompDeferred<NCVehicleInteriorOccupantComponent>(user);
        }
    }

    private void RegisterTrackedOccupant(EntityUid vehicle, EntityUid occupant, NCVehicleInteriorComponent interior)
    {
        interior.Passengers.Add(occupant);
    }

    private void UnregisterTrackedOccupant(EntityUid vehicle, EntityUid occupant)
    {
        if (TryComp(vehicle, out NCVehicleInteriorComponent? interior))
            interior.Passengers.Remove(occupant);
    }

    private void PruneTrackedOccupants(EntityUid vehicle, NCVehicleInteriorComponent interior)
    {
        interior.Passengers.RemoveWhere(p => TerminatingOrDeleted(p) ||
                                           !TryComp(p, out NCVehicleInteriorOccupantComponent? occupant) ||
                                           occupant.Vehicle != vehicle);
    }

    public bool TryGetVehicleFromInterior(EntityUid interiorEnt, [NotNullWhen(true)] out EntityUid? vehicle)

    {
        vehicle = null;
        if (TryComp(interiorEnt, out NCVehicleInteriorLinkComponent? link))
        {
            vehicle = link.Vehicle;
            return true;
        }

        var mapId = _transform.GetMapId(interiorEnt);
        var mapUid = _mapManager.GetMapEntityId(mapId);
        if (TryComp(mapUid, out link))
        {
            vehicle = link.Vehicle;
            return true;
        }

        return false;
    }
}
