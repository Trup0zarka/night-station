using System.Numerics;
using Content.Shared._NC.Vehicle.Components;
using Content.Shared._NC.Vehicle.Grid.Components;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;

namespace Content.Shared._NC.Vehicle.Grid;

public sealed partial class GridVehicleMoverSystem
{
    private const int MaxTileStepsPerFrame = 4;

    private void UpdateMovement(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        VehicleComponent vehicle,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i inputDir,
        bool pushing,
        float frameTime)
    {
        if (vehicle.Operator != null)
        {
            var canRunEvent = new VehicleCanRunEvent((uid, vehicle));
            RaiseLocalEvent(uid, ref canRunEvent);
            if (!canRunEvent.CanRun)
            {
                mover.CurrentSpeed = 0f;
                mover.IsCommittedToMove = false;
                mover.IsMoving = false;
                SetGridPosition(uid, grid, mover.Position);
                Dirty(uid, mover);
                return;
            }
        }

        var isPushMove = mover.IsPushMove || pushing;
        if (mover.IsCommittedToMove)
        {
            if (mover.IsPushMove)
                ContinueCommittedPush(uid, mover, grid, gridComp, inputDir, frameTime);
            else
                ContinueCommittedMove(uid, mover, grid, gridComp, inputDir, frameTime);
            return;
        }

        var hasInput = inputDir != Vector2i.Zero;

        if (!hasInput)
        {
            mover.CurrentSpeed = GridVehicleMotionSimulator.StepIdleSpeed(
                mover.CurrentSpeed,
                mover.Deceleration,
                frameTime);

            var tile = GetTile(grid, gridComp, mover.Position);
            mover.CurrentTile = tile;
            mover.TargetTile = tile;
            mover.IsMoving = MathF.Abs(mover.CurrentSpeed) > 0.01f;
            if (!mover.IsMoving)
                mover.IsPushMove = false;
            
            // TODO: Звуки
            // if (mover.IsMoving && !isPushMove)
            //    PlayRunningSound(uid);

            SetGridPosition(uid, grid, mover.Position);
            Dirty(uid, mover);
            return;
        }

        if (pushing)
            CommitPushTile(uid, mover, grid, gridComp, inputDir);
        else
            CommitNextTile(uid, mover, grid, gridComp, inputDir);
            
        SetGridPosition(uid, grid, mover.Position);
        Dirty(uid, mover);
    }

    private void ContinueCommittedPush(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i inputDir,
        float frameTime)
    {
        var tileDelta = mover.TargetTile - mover.CurrentTile;
        if (tileDelta == Vector2i.Zero)
        {
            mover.IsCommittedToMove = false;
            mover.IsPushMove = false;
            return;
        }

        var maxSpeed = mover.MaxSpeed;
        var accelModifier = 1f;

        var hasInput = inputDir != Vector2i.Zero;
        mover.CurrentSpeed = GridVehicleMotionSimulator.StepPushSpeed(
            mover.CurrentSpeed,
            maxSpeed,
            mover.Acceleration * accelModifier,
            mover.Deceleration,
            hasInput,
            mover.IsCommittedToMove,
            frameTime);

        var remaining = MathF.Abs(mover.CurrentSpeed) * frameTime;
        var steps = 0;

        while (true)
        {
            var advance = GridVehicleMotionSimulator.AdvanceToTarget(
                mover.Position,
                mover.CurrentTile,
                mover.TargetTile,
                remaining);

            mover.Position = advance.Position;
            mover.CurrentTile = advance.CurrentTile;
            remaining = advance.RemainingDistance;

            if (!advance.ReachedTarget)
                break;

            if (!hasInput || MathF.Abs(mover.CurrentSpeed) <= 0.0001f)
            {
                mover.IsCommittedToMove = false;
                mover.IsPushMove = false;
                mover.CurrentSpeed = 0f;
                break;
            }

            CommitPushTile(uid, mover, grid, gridComp, inputDir);
            if (!mover.IsCommittedToMove)
                break;

            if (++steps >= MaxTileStepsPerFrame || remaining <= 0f)
                break;
        }

        mover.IsMoving = MathF.Abs(mover.CurrentSpeed) > 0.01f;
        SetGridPosition(uid, grid, mover.Position);
        _physics.WakeBody(uid);
        Dirty(uid, mover);
    }

    private void ContinueCommittedMove(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i inputDir,
        float frameTime)
    {
        var tileDelta = mover.TargetTile - mover.CurrentTile;
        if (tileDelta == Vector2i.Zero)
        {
            mover.IsCommittedToMove = false;
            return;
        }

        if (inputDir != Vector2i.Zero)
        {
            var stepDir = new Vector2i(Math.Sign(tileDelta.X), Math.Sign(tileDelta.Y));
            if (stepDir != Vector2i.Zero && inputDir == -stepDir && MathF.Abs(mover.CurrentSpeed) > 0.01f)
            {
                var tileAtPos = GetTile(grid, gridComp, mover.Position);
                mover.CurrentTile = tileAtPos;
                mover.TargetTile = tileAtPos;
                mover.IsCommittedToMove = false;
                mover.IsPushMove = false;
                mover.CurrentSpeed = 0f;
                mover.IsMoving = false;
                SetGridPosition(uid, grid, mover.Position);
                _physics.WakeBody(uid);
                Dirty(uid, mover);
                return;
            }
        }

        var maxSpeed = mover.MaxSpeed;
        var maxReverseSpeed = mover.MaxReverseSpeed;
        var accelModifier = 1f;

        var hasInput = inputDir != Vector2i.Zero;
        var speedResult = GridVehicleMotionSimulator.StepDriveSpeed(
            mover.CurrentSpeed,
            new GridVehicleMotionSimulator.DriveProfile(
                maxSpeed,
                maxReverseSpeed,
                mover.Acceleration * accelModifier,
                mover.ReverseAcceleration * accelModifier,
                mover.Deceleration),
            mover.CurrentDirection,
            inputDir,
            hasInput,
            mover.IsCommittedToMove,
            frameTime);

        mover.CurrentSpeed = speedResult.CurrentSpeed;
        var remaining = MathF.Abs(mover.CurrentSpeed) * frameTime;
        var steps = 0;

        while (true)
        {
            var advance = GridVehicleMotionSimulator.AdvanceToTarget(
                mover.Position,
                mover.CurrentTile,
                mover.TargetTile,
                remaining);

            mover.Position = advance.Position;
            mover.CurrentTile = advance.CurrentTile;
            remaining = advance.RemainingDistance;

            if (!advance.ReachedTarget)
                break;

            if (!hasInput || MathF.Abs(mover.CurrentSpeed) <= 0.0001f)
            {
                mover.IsCommittedToMove = false;
                mover.CurrentSpeed = 0f;
                break;
            }

            if (speedResult.ChangingDirection)
            {
                mover.IsCommittedToMove = false;
                mover.CurrentSpeed = 0f;
                break;
            }

            CommitNextTile(uid, mover, grid, gridComp, inputDir);
            if (!mover.IsCommittedToMove)
                break;

            if (++steps >= MaxTileStepsPerFrame || remaining <= 0f)
                break;
        }
        mover.IsMoving = MathF.Abs(mover.CurrentSpeed) > 0.01f;
        
        // TODO: Звуки
        // if (mover.IsMoving)
        //    PlayRunningSound(uid);

        SetGridPosition(uid, grid, mover.Position);
        _physics.WakeBody(uid);
        Dirty(uid, mover);
    }

    private void CommitNextTile(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i inputDir)
    {
        if (inputDir == Vector2i.Zero)
        {
            mover.IsCommittedToMove = false;
            mover.IsPushMove = false;
            return;
        }

        var facing = mover.CurrentDirection;
        var hadFacing = facing != Vector2i.Zero;

        if (!hadFacing)
            facing = inputDir;

        var reversing = hadFacing && inputDir == -facing;
        var turnRequested = !reversing && hadFacing && inputDir != facing;
        var canTurnNow = !turnRequested || CanApplyTurn(mover);
        var turnInPlaceMaxSpeed = MathF.Max(0f, mover.TurnInPlaceMaxSpeed);
        var atStop = MathF.Abs(mover.CurrentSpeed) <= turnInPlaceMaxSpeed;

        if (mover.TurnInPlace &&
            atStop &&
            !reversing &&
            (turnRequested || !hadFacing))
        {
            if (!CanApplyTurn(mover))
            {
                mover.TargetTile = mover.CurrentTile;
                mover.IsCommittedToMove = false;
                mover.IsPushMove = false;
                return;
            }

            var desiredFacing = inputDir;
            var desiredTurnRot = new Vector2(desiredFacing.X, desiredFacing.Y).ToWorldAngle();
            var currentCenter = new Vector2(mover.CurrentTile.X + 0.5f, mover.CurrentTile.Y + 0.5f);

            if (CanOccupyTransform(uid, mover, grid, currentCenter, desiredTurnRot, Clearance))
            {
                var turned = mover.CurrentDirection != desiredFacing;
                mover.CurrentDirection = desiredFacing;
                if (turned)
                {
                    StartTurnDelay(mover);
                    if (mover.TurnDelay > 0f)
                        mover.InPlaceTurnBlockUntil = _timing.CurTime + TimeSpan.FromSeconds(mover.TurnDelay);
                }

                _transform.SetLocalRotation(uid, desiredTurnRot);
                mover.TargetTile = mover.CurrentTile;
                mover.IsCommittedToMove = false;
                mover.IsPushMove = false;
                mover.CurrentSpeed = 0f;
                return;
            }
        }

        if (!reversing && canTurnNow)
            facing = inputDir;
        else if (turnRequested && !canTurnNow && MathF.Abs(mover.CurrentSpeed) <= 0.01f)
        {
            mover.TargetTile = mover.CurrentTile;
            mover.IsCommittedToMove = false;
            mover.IsPushMove = false;
            return;
        }

        if (mover.TurnInPlace &&
            !reversing &&
            atStop &&
            mover.InPlaceTurnBlockUntil > _timing.CurTime)
        {
            mover.TargetTile = mover.CurrentTile;
            mover.IsCommittedToMove = false;
            mover.IsPushMove = false;
            return;
        }

        Vector2i moveDir;

        if (reversing)
            moveDir = -facing;
        else
            moveDir = facing;

        var targetTile = mover.CurrentTile + moveDir;
        var targetCenter = new Vector2(targetTile.X + 0.5f, targetTile.Y + 0.5f);
        var desiredRot = new Vector2(facing.X, facing.Y).ToWorldAngle();

        if (!CanOccupyTransform(uid, mover, grid, targetCenter, desiredRot, Clearance))
        {
            // Try to at least rotate in place if there's room on the current tile.
            if (!reversing && canTurnNow)
            {
                var currentCenter = new Vector2(mover.CurrentTile.X + 0.5f, mover.CurrentTile.Y + 0.5f);
                if (CanOccupyTransform(uid, mover, grid, currentCenter, desiredRot, Clearance))
                {
                    var turned = mover.CurrentDirection != facing;
                    mover.CurrentDirection = facing;
                    if (turned && hadFacing)
                        StartTurnDelay(mover);
                    _transform.SetLocalRotation(uid, desiredRot);
                }
            }

            mover.TargetTile = mover.CurrentTile;
            mover.IsCommittedToMove = false;
            mover.IsPushMove = false;
            mover.CurrentSpeed = 0f;
            return;
        }

        if (!reversing)
        {
            var turned = mover.CurrentDirection != facing;
            mover.CurrentDirection = facing;
            if (turned && hadFacing)
                StartTurnDelay(mover);
            _transform.SetLocalRotation(uid, desiredRot);
        }

        mover.TargetTile = targetTile;
        mover.IsCommittedToMove = true;
        mover.IsPushMove = false;
    }

    private void CommitPushTile(
        EntityUid uid,
        GridVehicleMoverComponent mover,
        EntityUid grid,
        MapGridComponent gridComp,
        Vector2i inputDir)
    {
        if (inputDir == Vector2i.Zero)
        {
            mover.IsCommittedToMove = false;
            mover.IsPushMove = false;
            return;
        }

        var targetTile = mover.CurrentTile + inputDir;
        var targetCenter = new Vector2(targetTile.X + 0.5f, targetTile.Y + 0.5f);

        if (!CanOccupyTransform(uid, mover, grid, targetCenter, null, Clearance))
        {
            mover.TargetTile = mover.CurrentTile;
            mover.IsCommittedToMove = false;
            mover.IsPushMove = false;
            mover.CurrentSpeed = 0f;
            return;
        }

        if (mover.PushCooldown > 0f)
            mover.NextPushTime = _timing.CurTime + TimeSpan.FromSeconds(mover.PushCooldown);

        mover.TargetTile = targetTile;
        mover.IsCommittedToMove = true;
        mover.IsPushMove = true;
    }

    private bool CanApplyTurn(GridVehicleMoverComponent mover)
    {
        if (mover.TurnDelay <= 0f)
            return true;

        return _timing.CurTime >= mover.NextTurnTime;
    }

    private void StartTurnDelay(GridVehicleMoverComponent mover)
    {
        if (mover.TurnDelay <= 0f)
            return;

        mover.NextTurnTime = _timing.CurTime + TimeSpan.FromSeconds(mover.TurnDelay);
    }

    private void SetGridPosition(EntityUid uid, EntityUid grid, Vector2 gridPos)
    {
        var xform = Transform(uid);
        if (!xform.ParentUid.IsValid())
            return;

        var coords = new EntityCoordinates(grid, gridPos);
        var local = coords.WithEntityId(xform.ParentUid, _transform, EntityManager).Position;

        _transform.SetLocalPosition(uid, local, xform);
    }

    private Vector2i GetTile(EntityUid grid, MapGridComponent gridComp, Vector2 pos)
    {
        var coords = new EntityCoordinates(grid, pos);
        return _map.TileIndicesFor(grid, gridComp, coords);
    }
}
