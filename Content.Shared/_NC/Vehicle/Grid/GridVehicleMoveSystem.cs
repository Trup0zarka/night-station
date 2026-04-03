using System.Numerics;
using Content.Shared._NC.Vehicle.Components;
using Content.Shared._NC.Vehicle.Grid.Components;
using Content.Shared.Damage;
using Content.Shared.Doors.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Events;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Shared._NC.Vehicle.Grid;

public sealed partial class GridVehicleMoverSystem : EntitySystem
{
    private enum VehicleCollisionClass : byte
    {
        None,
        Static,
        Mob,
        Vehicle,
        SoftMob,
        Ignore,
        Breakable,
        Hard
    }

    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedDoorSystem _door = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly ISharedPlayerManager _player = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<PhysicsComponent> _physicsQuery;
    private EntityQuery<FixturesComponent> _fixturesQuery;
    private EntityQuery<TransformComponent> _xformQuery;

    private const float Clearance = PhysicsConstants.PolygonRadius * 0.75f;
    private const int GridVehicleStaticBlockerMask =
        (int) (CollisionGroup.Impassable |
               CollisionGroup.HighImpassable |
               CollisionGroup.LowImpassable |
               CollisionGroup.MidImpassable);

    private const float MovementFixedStep = 1f / 60f;
    private const int MaxFixedStepsPerFrame = 6;
    private const float ClientSmoothingSnapDistance = 1.25f;
    private const float ClientSmoothingRate = 22f;

    private readonly Dictionary<EntityUid, bool> _hardState = new();
    private readonly Dictionary<EntityUid, float> _movementAccumulator = new();
    
    // Missing Debug structures from RMC
    private readonly List<(EntityUid, Vector2i)> DebugTestedTiles = new();
    private readonly List<DebugCollision> DebugCollisions = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastMobCollision = new();
    private readonly Dictionary<EntityUid, bool> _lastMobPushAxis = new();
    private const float PushOverlapEpsilon = 0.01f;
    private const float PushAxisHysteresis = 0.05f;
    private const float PushTileBlockFraction = 0.5f;
    private const float PushWallOverlapArea = 0.1f;
    private const float PushWallSkin = 0.05f;
    private const int GridVehiclePushHardBlockMask = (int) CollisionGroup.Impassable;
    private readonly TimeSpan MobCollisionCooldown = TimeSpan.FromSeconds(1);
    private readonly float MobCollisionKnockdown = 3f;
    private readonly EntProtoId CollisionDamageType = "Blunt";
    private readonly float MobCollisionDamage = 10f;

    public override void Initialize()
    {
        base.Initialize();
        _gridQuery = GetEntityQuery<MapGridComponent>();
        _physicsQuery = GetEntityQuery<PhysicsComponent>();
        _fixturesQuery = GetEntityQuery<FixturesComponent>();
        _xformQuery = GetEntityQuery<TransformComponent>();

        SubscribeLocalEvent<GridVehicleMoverComponent, ComponentStartup>(OnMoverStartup);
        SubscribeLocalEvent<GridVehicleMoverComponent, ComponentShutdown>(OnMoverShutdown);
        SubscribeLocalEvent<GridVehicleMoverComponent, MoveEvent>(OnMoverMove);
        SubscribeLocalEvent<GridVehicleMoverComponent, ReAnchorEvent>(OnMoverReAnchor);
        SubscribeLocalEvent<GridVehicleMoverComponent, VehicleCanRunEvent>(OnMoverCanRun);
        SubscribeLocalEvent<GridVehicleMoverComponent, PreventCollideEvent>(OnMoverPreventCollide);
    }

    private void OnMoverStartup(Entity<GridVehicleMoverComponent> ent, ref ComponentStartup args)
    {
        TrySyncMoverToCurrentGrid(ent, centerOnTile: true, force: true);
    }

    private void OnMoverShutdown(Entity<GridVehicleMoverComponent> ent, ref ComponentShutdown args)
    {
        _hardState.Remove(ent.Owner);
        _movementAccumulator.Remove(ent.Owner);
    }

    private void OnMoverMove(Entity<GridVehicleMoverComponent> ent, ref MoveEvent args)
    {
        if (!args.ParentChanged)
            return;

        TrySyncMoverToCurrentGrid(ent, centerOnTile: false);
    }

    private void OnMoverReAnchor(Entity<GridVehicleMoverComponent> ent, ref ReAnchorEvent args)
    {
        TrySyncMoverToCurrentGrid(ent, centerOnTile: false);
    }

    private bool TrySyncMoverToCurrentGrid(
        Entity<GridVehicleMoverComponent> ent,
        bool centerOnTile,
        TransformComponent? xform = null,
        bool force = false)
    {
        var uid = ent.Owner;
        xform ??= Transform(uid);

        if (xform.GridUid is not { } grid || !_gridQuery.TryComp(grid, out var gridComp))
        {
            if (ent.Comp.SyncedGrid == null)
                return false;

            ent.Comp.SyncedGrid = null;
            ent.Comp.CurrentSpeed = 0f;
            ent.Comp.IsCommittedToMove = false;
            ent.Comp.IsPushMove = false;
            ent.Comp.IsMoving = false;
            _hardState[uid] = true;
            _movementAccumulator[uid] = 0f;
            Dirty(uid, ent.Comp);
            return true;
        }

        if (!force && ent.Comp.SyncedGrid == grid)
            return false;

        var coords = xform.Coordinates.WithEntityId(grid, _transform, EntityManager);
        var tile = _map.TileIndicesFor(grid, gridComp, coords);

        ent.Comp.SyncedGrid = grid;
        ent.Comp.CurrentTile = tile;
        ent.Comp.TargetTile = tile;
        ent.Comp.Position = centerOnTile
            ? new Vector2(tile.X + 0.5f, tile.Y + 0.5f)
            : coords.Position;
        ent.Comp.CurrentSpeed = 0f;
        ent.Comp.NextPushTime = TimeSpan.Zero;
        ent.Comp.NextTurnTime = TimeSpan.Zero;
        ent.Comp.InPlaceTurnBlockUntil = TimeSpan.Zero;
        ent.Comp.IsCommittedToMove = false;
        ent.Comp.IsPushMove = false;
        ent.Comp.IsMoving = false;
        _hardState[uid] = true;
        _movementAccumulator[uid] = 0f;

        Dirty(uid, ent.Comp);
        return true;
    }

    private void OnMoverCanRun(Entity<GridVehicleMoverComponent> ent, ref VehicleCanRunEvent args)
    {
        if (!args.CanRun)
            return;

        if (!TryComp(ent.Owner, out VehicleComponent? vehicle) || vehicle.Operator is not { } operatorUid)
            return;
    }

    private void OnMoverPreventCollide(Entity<GridVehicleMoverComponent> ent, ref PreventCollideEvent args)
    {
        if (args.Cancelled)
            return;

        if (!TryComp(ent.Owner, out VehicleComponent? vehicle) || vehicle.MovementKind != VehicleMovementKind.Grid)
            return;

        if (args.OtherEntity == ent.Owner)
            return;

        if (args.OtherBody.BodyType != BodyType.Static)
            return;

        if ((args.OtherFixture.CollisionLayer & GridVehicleStaticBlockerMask) == 0)
            return;

        args.Cancelled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var q = EntityQueryEnumerator<GridVehicleMoverComponent, VehicleComponent, TransformComponent>();

        while (q.MoveNext(out var uid, out var mover, out var vehicle, out var xform))
        {
            if (vehicle.MovementKind != VehicleMovementKind.Grid)
                continue;

            TrySyncMoverToCurrentGrid((uid, mover), centerOnTile: false, xform);

            if (xform.GridUid is not { } grid || !_gridQuery.TryComp(grid, out var gridComp))
                continue;

            if (_net.IsClient && !ShouldPredictVehicleMovement(vehicle))
            {
                SmoothReplicatedVehicle(uid, grid, mover, frameTime);
                continue;
            }

            var inputDir = GetMoverInput(uid, mover, vehicle, out var pushing);
            var accumulator = _movementAccumulator.GetValueOrDefault(uid) + frameTime;
            var maxAccum = MovementFixedStep * MaxFixedStepsPerFrame;
            if (accumulator > maxAccum)
                accumulator = maxAccum;

            var steps = 0;
            while (accumulator >= MovementFixedStep && steps < MaxFixedStepsPerFrame)
            {
                var currentXform = Transform(uid);
                TrySyncMoverToCurrentGrid((uid, mover), centerOnTile: false, currentXform);
                if (currentXform.GridUid is not { } currentGrid || !_gridQuery.TryComp(currentGrid, out var currentGridComp))
                    break;

                UpdateMovement(uid, mover, vehicle, currentGrid, currentGridComp, inputDir, pushing, MovementFixedStep);
                accumulator -= MovementFixedStep;
                steps++;
            }

            _movementAccumulator[uid] = accumulator;
        }
    }

    private bool ShouldPredictVehicleMovement(VehicleComponent vehicle)
    {
        if (!_net.IsClient)
            return true;

        if (!_timing.InPrediction)
            return false;

        return vehicle.Operator != null && vehicle.Operator == _player.LocalEntity;
    }

    private void SmoothReplicatedVehicle(EntityUid uid, EntityUid grid, GridVehicleMoverComponent mover, float frameTime)
    {
        var xform = Transform(uid);
        if (!xform.ParentUid.IsValid())
            return;

        var coords = new EntityCoordinates(grid, mover.Position);
        var target = coords.WithEntityId(xform.ParentUid, _transform, EntityManager).Position;
        var current = xform.LocalPosition;
        var delta = target - current;

        if (delta.LengthSquared() >= ClientSmoothingSnapDistance * ClientSmoothingSnapDistance)
        {
            _transform.SetLocalPosition(uid, target, xform);
            return;
        }

        var alpha = 1f - MathF.Exp(-ClientSmoothingRate * frameTime);
        var smoothed = Vector2.Lerp(current, target, alpha);
        _transform.SetLocalPosition(uid, smoothed, xform);
    }

    private struct DebugCollision
    {
        public EntityUid Vehicle;
        public EntityUid Other;
        public Box2 VehicleAabb;
        public Box2 OtherAabb;
        public float Damage;
        public float Speed;
        public float Clearance;
        public MapId MapId;

        public DebugCollision(EntityUid vehicle, EntityUid other, Box2 vehicleAabb, Box2 otherAabb, float damage, float speed, float clearance, MapId mapId)
        {
            Vehicle = vehicle;
            Other = other;
            VehicleAabb = vehicleAabb;
            OtherAabb = otherAabb;
            Damage = damage;
            Speed = speed;
            Clearance = clearance;
            MapId = mapId;
        }
    }
}
