using System.Numerics;
using Content.Shared._NC.Vehicle.Components;
using Content.Shared._NC.Vehicle.Grid.Components;
using Content.Shared.Damage;
using Content.Shared.Doors.Components;
using Content.Shared.Foldable;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;

namespace Content.Shared._NC.Vehicle.Grid;

public sealed partial class GridVehicleMoverSystem
{
    private bool CanOccupyTransform(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        Vector2 gridPos,
        Angle? overrideRotation,
        float clearance)
    {
        if (!_physicsQuery.TryComp(uid, out var body) || !_fixturesQuery.TryComp(uid, out var fixtures))
            return true;

        EntityUid? operatorUid = null;
        if (TryComp<VehicleComponent>(uid, out var vehicleComp))
            operatorUid = vehicleComp.Operator;

        if (!body.CanCollide)
            return true;

        if (!_gridQuery.TryComp(grid, out var gridComp))
            return true;

        var coords = new EntityCoordinates(grid, gridPos);
        var world = _transform.ToMapCoordinates(coords);

        var tileIndices = _map.TileIndicesFor(grid, gridComp, coords);
        DebugTestedTiles.Add((grid, tileIndices));

        var rotation = overrideRotation ?? _transform.GetWorldRotation(uid);
        var tx = new Transform(world.Position, (float) rotation.Theta);

        var wheelDamage = _net.IsClient ? 0f : GetWheelCollisionDamage(uid, mover);

        if (!TryGetFixtureAabb(fixtures, tx, out var aabb))
            return true;

        var hits = _lookup.GetEntitiesIntersecting(world.MapId, aabb, LookupFlags.Dynamic | LookupFlags.Static);
        var playedCollisionSound = false;
        var blocked = false;
        var mobHits = new HashSet<EntityUid>();

        foreach (var other in hits)
        {
            if (other == uid)
                continue;

            if (!_physicsQuery.TryComp(other, out var otherBody) || !otherBody.CanCollide)
                continue;

            if (!_fixturesQuery.TryComp(other, out var otherFixtures))
                continue;

            var otherXform = Transform(other);
            var otherTx = _physics.GetPhysicsTransform(other, otherXform);

            if (!TryGetFixtureAabb(otherFixtures, otherTx, out var otherAabb))
                continue;

            if (!aabb.Intersects(otherAabb))
                continue;

            var hardCollidable = _physics.IsHardCollidable((uid, fixtures, body), (other, otherFixtures, otherBody));
            var hasDoor = TryComp(other, out DoorComponent? door);
            var isFoldable = HasComp<FoldableComponent>(other);
            var isMob = TryComp(other, out MobStateComponent? mob);
            var collisionClass = ClassifyCollisionCandidate(other, otherXform, otherBody, hardCollidable, isMob, isFoldable, hasDoor);

            if (collisionClass == VehicleCollisionClass.SoftMob)
            {
                PlayMobCollisionSound(uid, ref playedCollisionSound);
                if (!PushMobOutOfVehicle(uid, other, aabb, otherAabb))
                {
                    ApplyWheelCollisionDamage(uid, mover, wheelDamage);
                    DebugCollisions.Add(new DebugCollision(uid, other, aabb, otherAabb, 0f, 0f, clearance, world.MapId));
                    return false;
                }

                continue;
            }

            if (hasDoor && !_net.IsClient)
            {
                _door.TryOpen(other, door, operatorUid);
            }

            if (collisionClass == VehicleCollisionClass.Ignore)
                continue;

            if (collisionClass == VehicleCollisionClass.Breakable)
            {
                if (TrySmash(other, uid, ref playedCollisionSound))
                    continue;
                continue;
            }

            if (collisionClass == VehicleCollisionClass.Hard)
            {
                PlayCollisionSound(uid, ref playedCollisionSound);
                ApplyWheelCollisionDamage(uid, mover, wheelDamage);
                DebugCollisions.Add(new DebugCollision(uid, other, aabb, otherAabb, 0f, 0f, clearance, world.MapId));
                blocked = true;
                break;
            }

            if (_net.IsClient && isMob && mob != null && ShouldPredictVehicleInteractions(uid))
                PredictRunover(uid, other, mob);

            if (!_net.IsClient && isMob && mob != null)
                mobHits.Add(other);
        }

        if (blocked)
            return false;

        if (!_net.IsClient)
        {
            foreach (var mobUid in mobHits)
            {
                if (!TryComp(mobUid, out MobStateComponent? mob))
                    continue;

                HandleMobCollision(uid, mobUid, mob, ref playedCollisionSound);
            }
        }

        return true;
    }

    private void ApplyWheelCollisionDamage(EntityUid vehicle, GridVehicleMoverComponent mover, float damage)
    {
        if (_net.IsClient || damage <= 0f)
            return;
    }

    private float GetWheelCollisionDamage(EntityUid vehicle, GridVehicleMoverComponent mover)
    {
        return 0f;
    }

    private bool TryGetFixtureAabb(FixturesComponent fixtures, Transform transformData, out Box2 aabb)
    {
        var first = true;
        aabb = default;

        foreach (var fixture in fixtures.Fixtures.Values)
        {
            if (!fixture.Hard)
                continue;

            for (var i = 0; i < fixture.Shape.ChildCount; i++)
            {
                var child = fixture.Shape.ComputeAABB(transformData, i);

                if (first)
                {
                    aabb = child;
                    first = false;
                }
                else
                {
                    aabb = aabb.Union(child);
                }
            }
        }

        return !first;
    }

    private bool TrySmash(EntityUid target, EntityUid vehicle, ref bool playedCollisionSound)
    {
        return true;
    }

    private void PlayCollisionSound(EntityUid uid, ref bool played)
    {
    }

    private void PlayMobCollisionSound(EntityUid uid, ref bool played)
    {
    }

    private void HandleMobCollision(EntityUid vehicle, EntityUid target, MobStateComponent mobState, ref bool playedCollisionSound)
    {
        if (_net.IsClient || _mobState.IsDead(target, mobState))
            return;

        var now = _timing.CurTime;
        if (_lastMobCollision.TryGetValue(target, out var last) && now < last + MobCollisionCooldown)
            return;

        _lastMobCollision[target] = now;

        PlayMobCollisionSound(vehicle, ref playedCollisionSound);

        var damage = new DamageSpecifier
        {
            DamageDict =
            {
                [CollisionDamageType] = MobCollisionDamage,
            },
        };

        _damageable.TryChangeDamage(target, damage);

        _stun.TryKnockdown(target, TimeSpan.FromSeconds(MobCollisionKnockdown), true);

        if (_physicsQuery.TryComp(target, out var targetBody))
        {
            _physics.SetLinearVelocity(target, Vector2.Zero, body: targetBody);
            _physics.SetAngularVelocity(target, 0f, body: targetBody);
        }
    }

    private bool PushMobOutOfVehicle(EntityUid vehicle, EntityUid mob, Box2 vehicleAabb, Box2 mobAabb)
    {
        var xform = Transform(mob);
        if (xform.Anchored)
        {
            return false;
        }

        var centeredAabb = GetCenteredMobAabb(mob, mobAabb);

        if (!TryGetMobPush(vehicle, mob, vehicleAabb, centeredAabb, out var target, out _))
        {
            return false;
        }

        if (_net.IsClient)
        {
            if (ShouldPredictVehicleInteractions(vehicle))
                ApplyMobPush(mob, target);
        }
        else
        {
            ApplyMobPush(mob, target);
        }

        return true;
    }

    private Box2 GetCenteredMobAabb(EntityUid mob, Box2 mobAabb)
    {
        var mobPos = _transform.GetWorldPosition(mob);
        var delta = mobAabb.Center - mobPos;
        if (delta.LengthSquared() <= 0.0001f)
            return mobAabb;

        return Box2.CenteredAround(mobPos, mobAabb.Size);
    }

    private void ApplyMobPush(EntityUid mob, EntityCoordinates target)
    {
        if (target == EntityCoordinates.Invalid)
            return;

        var mobMap = _transform.GetMapCoordinates(mob);
        var targetMap = _transform.ToMapCoordinates(target);
        if (mobMap.MapId != targetMap.MapId)
            return;

        if (_physicsQuery.TryComp(mob, out var mobBody))
        {
            _physics.SetLinearVelocity(mob, Vector2.Zero, body: mobBody);
            _physics.SetAngularVelocity(mob, 0f, body: mobBody);
        }

        var mobXform = Transform(mob);
        _transform.SetCoordinates(mob, mobXform, target);
    }

    private bool ShouldPredictVehicleInteractions(EntityUid vehicle)
    {
        if (!_net.IsClient || !_timing.InPrediction)
            return false;

        if (!_physicsQuery.TryComp(vehicle, out var vehicleBody) || !vehicleBody.Predict)
            return false;

        if (!TryComp(vehicle, out VehicleComponent? vehicleComp))
            return false;

        return vehicleComp.Operator != null && vehicleComp.Operator == _player.LocalEntity;
    }

    private void PredictRunover(EntityUid vehicle, EntityUid mob, MobStateComponent mobState)
    {
        if (!ShouldPredictVehicleInteractions(vehicle))
            return;

        if (_mobState.IsDead(mob, mobState) || _standing.IsDown(mob))
            return;

        _stun.TryKnockdown(mob, TimeSpan.FromSeconds(MobCollisionKnockdown), true);

        if (_physicsQuery.TryComp(mob, out var mobBody))
        {
            _physics.SetLinearVelocity(mob, Vector2.Zero, body: mobBody);
            _physics.SetAngularVelocity(mob, 0f, body: mobBody);
        }
    }

    private bool TryGetMobPush(
        EntityUid vehicle,
        EntityUid mob,
        Box2 vehicleAabb,
        Box2 mobAabb,
        out EntityCoordinates target,
        out string reason)
    {
        target = EntityCoordinates.Invalid;
        reason = "unknown";

        var vehicleHalf = vehicleAabb.Size / 2f;
        var mobHalf = mobAabb.Size / 2f;

        var vehicleCenter = vehicleAabb.Center;
        var mobCenter = mobAabb.Center;

        var diff = mobCenter - vehicleCenter;
        var overlapX = vehicleHalf.X + mobHalf.X - Math.Abs(diff.X);
        var overlapY = vehicleHalf.Y + mobHalf.Y - Math.Abs(diff.Y);

        if (overlapX <= 0f || overlapY <= 0f)
        {
            reason = $"no overlap overlapX={overlapX:F3} overlapY={overlapY:F3}";
            return false;
        }

        if (overlapX <= PushOverlapEpsilon && overlapY <= PushOverlapEpsilon)
        {
            reason = $"overlap below epsilon overlapX={overlapX:F3} overlapY={overlapY:F3}";
            return false;
        }

        var pushX = overlapX > 0f
            ? new Vector2(Math.Sign(diff.X == 0f ? 1f : diff.X) * overlapX, 0f)
            : Vector2.Zero;
        var pushY = overlapY > 0f
            ? new Vector2(0f, Math.Sign(diff.Y == 0f ? 1f : diff.Y) * overlapY)
            : Vector2.Zero;

        var vehicleBounds = vehicleAabb;
        var useX = overlapX < overlapY;
        if (MathF.Abs(overlapX - overlapY) <= PushAxisHysteresis &&
            _lastMobPushAxis.TryGetValue(mob, out var lastUseX))
        {
            useX = lastUseX;
        }

        var first = useX ? pushX : pushY;
        var second = useX ? pushY : pushX;

        if (TryGetSidePushTarget(vehicle, mob, mobAabb, vehicleBounds, first, out target, out reason))
        {
            _lastMobPushAxis[mob] = useX;
            return true;
        }

        var firstReason = reason;
        if (TryGetSidePushTarget(vehicle, mob, mobAabb, vehicleBounds, second, out target, out reason))
        {
            _lastMobPushAxis[mob] = !useX;
            return true;
        }

        reason = $"first={firstReason} second={reason}";
        return false;
    }

    private bool TryGetSidePushTarget(
        EntityUid vehicle,
        EntityUid mob,
        Box2 mobAabb,
        Box2 vehicleBounds,
        Vector2 push,
        out EntityCoordinates target,
        out string reason)
    {
        target = EntityCoordinates.Invalid;
        reason = "unknown";
        if (push == Vector2.Zero)
        {
            reason = "push zero";
            return false;
        }

        var adjusted = push;
        if (Math.Abs(adjusted.X) > 0f)
            adjusted.X += Math.Sign(adjusted.X) * Clearance;
        if (Math.Abs(adjusted.Y) > 0f)
            adjusted.Y += Math.Sign(adjusted.Y) * Clearance;

        var targetAabb = mobAabb.Translated(adjusted);
        if (targetAabb.Intersects(vehicleBounds))
        {
            reason = "target intersects vehicle bounds";
            return false;
        }

        if (IsPushBlocked(vehicle, mob, mobAabb, adjusted))
        {
            reason = "swept push blocked";
            return false;
        }

        var mobMap = _transform.GetMapCoordinates(mob);
        var mapCoords = new MapCoordinates(mobMap.Position + adjusted, mobMap.MapId);
        var mobXform = Transform(mob);
        if (mobXform.GridUid is { } grid && _gridQuery.TryComp(grid, out var gridComp))
        {
            var coords = _transform.ToCoordinates(grid, mapCoords);
            var indices = _map.TileIndicesFor(grid, gridComp, coords);
            if (IsPushTileBlocked(grid, gridComp, indices, vehicle, mob, out var blocker))
            {
                reason = $"tile blocked by {ToPrettyString(blocker)}";
                return false;
            }

            target = _transform.ToCoordinates(grid, mapCoords);
        }
        else
        {
            target = _transform.ToCoordinates(mapCoords);
        }

        if (target == EntityCoordinates.Invalid)
        {
            reason = "invalid target";
            return false;
        }

        return true;
    }

    private bool IsPushTileBlocked(
        EntityUid gridUid,
        MapGridComponent gridComp,
        Vector2i indices,
        EntityUid vehicle,
        EntityUid mob,
        out EntityUid blocker)
    {
        blocker = EntityUid.Invalid;
        var gridXform = Transform(gridUid);
        var (gridPos, gridRot, matrix) = _transform.GetWorldPositionRotationMatrix(gridXform, _xformQuery);

        var size = gridComp.TileSize;
        var localPos = new Vector2(indices.X * size + (size / 2f), indices.Y * size + (size / 2f));
        var worldPos = Vector2.Transform(localPos, matrix);

        var tileAabb = Box2.UnitCentered.Scale(0.95f * size);
        var worldBox = new Box2Rotated(tileAabb.Translated(worldPos), gridRot, worldPos);
        tileAabb = tileAabb.Translated(localPos);

        var tileArea = tileAabb.Width * tileAabb.Height;
        var minIntersectionArea = tileArea * PushTileBlockFraction;
        foreach (var ent in _lookup.GetEntitiesIntersecting(gridUid, worldBox, LookupFlags.Dynamic | LookupFlags.Static))
        {
            if (ent == vehicle || ent == mob)
                continue;

            if (IsDescendantOf(ent, vehicle) || IsDescendantOf(ent, mob))
                continue;

            var entXformComp = Transform(ent);
            if (HasComp<MobStateComponent>(ent) ||
                HasComp<FoldableComponent>(ent) ||
                TryComp<DoorComponent>(ent, out _))
            {
                continue;
            }

            if (HasComp<ItemComponent>(ent) && !entXformComp.Anchored)
                continue;

            if (!_physicsQuery.TryComp(ent, out var otherBody))
                continue;

            var isVehicle = HasComp<VehicleComponent>(ent);
            if (!entXformComp.Anchored && otherBody.BodyType != BodyType.Static && !isVehicle)
                continue;

            if (!_fixturesQuery.TryComp(ent, out var fixtures))
                continue;

            var (pos, rot) = _transform.GetWorldPositionRotation(entXformComp, _xformQuery);
            rot -= gridRot;
            pos = (-gridRot).RotateVec(pos - gridPos);
            var entXform = new Transform(pos, (float) rot.Theta);

            foreach (var fixture in fixtures.Fixtures.Values)
            {
                if (!fixture.Hard)
                    continue;

                if ((fixture.CollisionLayer & (int) GridVehiclePushHardBlockMask) == 0)
                    continue;

                for (var i = 0; i < fixture.Shape.ChildCount; i++)
                {
                    var intersection = fixture.Shape.ComputeAABB(entXform, i).Intersect(tileAabb);
                    var intersectionArea = intersection.Width * intersection.Height;
                    if (intersectionArea > minIntersectionArea)
                    {
                        blocker = ent;
                        return true;
                    }
                }
            }
        }

        return false;
    }

    private bool IsDescendantOf(EntityUid ent, EntityUid root)
    {
        if (ent == root)
            return true;

        var current = ent;
        while (current.IsValid())
        {
            var xform = Transform(current);
            var parent = xform.ParentUid;
            if (!parent.IsValid())
                return false;

            if (parent == root)
                return true;

            if (parent == xform.GridUid || parent == xform.MapUid)
                return false;

            current = parent;
        }

        return false;
    }

    private bool IsPushBlocked(EntityUid vehicle, EntityUid mob, Box2 mobAabb, Vector2 push)
    {
        if (push == Vector2.Zero)
            return false;

        var xform = Transform(mob);
        var mapId = xform.MapID;
        if (mapId == MapId.Nullspace)
            return false;

        if (!_physicsQuery.TryComp(mob, out var mobBody) || !_fixturesQuery.TryComp(mob, out var mobFixtures))
            return false;

        var targetAabb = mobAabb.Translated(push);
        var checkAabb = targetAabb.Enlarged(-PushWallSkin);
        if (!checkAabb.IsValid())
            checkAabb = targetAabb;

        var hits = _lookup.GetEntitiesIntersecting(mapId, checkAabb, LookupFlags.Dynamic | LookupFlags.Static);
        foreach (var other in hits)
        {
            if (other == mob || other == vehicle)
                continue;

            if (IsDescendantOf(other, vehicle) || IsDescendantOf(other, mob))
                continue;

            if (!_physicsQuery.TryComp(other, out var otherBody) || !otherBody.CanCollide)
                continue;

            var otherXform = Transform(other);
            if (!_fixturesQuery.TryComp(other, out var otherFixtures))
                continue;

            if (HasComp<MobStateComponent>(other) ||
                HasComp<FoldableComponent>(other) ||
                TryComp<DoorComponent>(other, out _))
            {
                continue;
            }

            var wallLike = false;
            var overlaps = false;
            var otherTx = _physics.GetPhysicsTransform(other, otherXform);
            foreach (var fixture in otherFixtures.Fixtures.Values)
            {
                if (!fixture.Hard)
                    continue;

                if ((fixture.CollisionLayer & (int) CollisionGroup.Impassable) != 0)
                {
                    wallLike = true;
                    for (var i = 0; i < fixture.Shape.ChildCount; i++)
                    {
                        var otherAabb = fixture.Shape.ComputeAABB(otherTx, i);
                        var intersection = otherAabb.Intersect(checkAabb);
                        if (Box2.Area(intersection) > PushWallOverlapArea)
                        {
                            overlaps = true;
                            break;
                        }
                    }

                    if (overlaps)
                        break;
                }
            }

            if (!wallLike || !overlaps)
                continue;

            if (_physics.IsHardCollidable((mob, mobFixtures, mobBody), (other, otherFixtures, otherBody)))
            {
                return true;
            }
        }

        return false;
    }

    private VehicleCollisionClass ClassifyCollisionCandidate(
        EntityUid other,
        TransformComponent otherXform,
        PhysicsComponent otherBody,
        bool hardCollidable,
        bool isMob,
        bool isFoldable,
        bool hasDoor)
    {
        var isVehicle = HasComp<VehicleComponent>(other);

        if (!otherXform.Anchored && HasComp<ItemComponent>(other))
            return VehicleCollisionClass.Ignore;

        var isLooseDynamic =
            !otherXform.Anchored &&
            otherBody.BodyType != BodyType.Static &&
            !isMob &&
            !isFoldable &&
            !isVehicle;

        if (isLooseDynamic)
            return VehicleCollisionClass.Ignore;

        if (isMob)
            return VehicleCollisionClass.SoftMob;

        if (hasDoor || isFoldable)
            return VehicleCollisionClass.Breakable;

        if (isFoldable && !hardCollidable)
            return VehicleCollisionClass.Ignore;

        return hardCollidable
            ? VehicleCollisionClass.Hard
            : VehicleCollisionClass.Ignore;
    }
}
