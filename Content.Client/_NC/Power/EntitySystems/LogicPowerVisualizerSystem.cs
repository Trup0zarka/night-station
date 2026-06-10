using Content.Shared._NC.Power.Components;
using Content.Client.Hands.Systems;
using Content.Shared.Inventory;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Input;
using Robust.Client.Player;
using Robust.Shared.Input;
using Robust.Shared.Timing;

namespace Content.Client._NC.Power.EntitySystems;

public sealed class LogicPowerVisualizerSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IInputManager _input = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IEyeManager _eye = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly InputSystem _inputSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private LogicPowerOverlay _overlay = default!;
    private bool _overlayEnabled = false;
    private bool _wasUseDown;
    public bool SandboxOverlayEnabled { get; private set; }

    public override void Initialize()
    {
        base.Initialize();
        // Overlay needs the live hand item and mouse world position to render the pending wire preview.
        _overlay = new LogicPowerOverlay(EntityManager, _transform, _hands, _input, _eye, _playerManager);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var player = _playerManager.LocalPlayer?.ControlledEntity;
        if (player == null)
        {
            SetOverlayEnabled(false);
            return;
        }

        // The overlay can come either from the worn visor or from the sandbox debug toggle.
        SetOverlayEnabled(HasARVisor(player.Value) || SandboxOverlayEnabled);
        UpdateFrozenVisualLink(player.Value);
    }

    private void UpdateFrozenVisualLink(EntityUid player)
    {
        if (!_timing.IsFirstTimePredicted)
            return;

        var useDown = _inputSystem.CmdStates.GetState(EngineKeyFunctions.Use) == BoundKeyState.Down;
        if (!useDown)
        {
            _wasUseDown = false;
            return;
        }

        if (_wasUseDown)
            return;

        _wasUseDown = true;

        if (!_overlayEnabled)
            return;

        var activeItem = _hands.GetActiveItem(player);
        if (activeItem == null ||
            !TryComp<WireSpoolComponent>(activeItem.Value, out var spool) ||
            spool.ActiveProvider == null)
        {
            return;
        }

        var providerUid = spool.ActiveProvider.Value;
        if (!TryComp<TransformComponent>(providerUid, out var providerXform))
            return;

        var mouseMapPos = _eye.PixelToMap(_input.MouseScreenPosition);
        if (mouseMapPos.MapId != providerXform.MapID)
            return;

        var providerPosition = _transform.GetWorldPosition(providerXform);
        if ((mouseMapPos.Position - providerPosition).Length() > spool.MaxDistance)
            return;

        _overlay.AddTemporaryFrozenPoint(providerUid, mouseMapPos);
    }

    private bool HasARVisor(EntityUid user)
    {
        if (_inventory.TryGetSlotEntity(user, "eyes", out var eyesItem))
        {
            if (HasComp<ARPowerVisorComponent>(eyesItem))
                return true;
        }
        return HasComp<ARPowerVisorComponent>(user);
    }

    private void SetOverlayEnabled(bool enabled)
    {
        if (_overlayEnabled == enabled)
            return;

        _overlayEnabled = enabled;
        if (enabled)
            _overlayManager.AddOverlay(_overlay);
        else
            _overlayManager.RemoveOverlay(_overlay);
    }

    public void SetSandboxOverlayEnabled(bool enabled)
    {
        // Store sandbox state separately so the normal visor path keeps working unchanged.
        SandboxOverlayEnabled = enabled;
    }

    public override void Shutdown()
    {
        base.Shutdown();
        _overlayManager.RemoveOverlay(_overlay);
    }
}
