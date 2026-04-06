using Content.Client._NC.FirstPerson.Components;
using Content.Client._NC.FirstPerson.Overlays;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Input;
using Robust.Shared.Input.Binding;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Player;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Robust.Shared.Maths;
using System.Linq;
using Robust.Shared.Utility;

namespace Content.Client._NC.FirstPerson.Systems;

public sealed partial class FirstPersonSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlayManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    private FirstPersonOverlay _overlay = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<FirstPersonComponent, ComponentInit>(OnFirstPersonInit);
        SubscribeLocalEvent<FirstPersonComponent, ComponentShutdown>(OnFirstPersonShutdown);
        SubscribeLocalEvent<FirstPersonComponent, LocalPlayerAttachedEvent>(OnPlayerAttached);
        SubscribeLocalEvent<FirstPersonComponent, LocalPlayerDetachedEvent>(OnPlayerDetached);

        _overlay = new FirstPersonOverlay();

        // Модуль 4 TDD: Перехват Use и UseSecondary
        CommandBinds.Builder
            .Bind(EngineKeyFunctions.Use, new PointerInputCmdHandler(OnUse))
            .Bind(EngineKeyFunctions.UseSecondary, new PointerInputCmdHandler(OnUseSecondary))
            .Register<FirstPersonSystem>();
    }

    private bool OnUse(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        return HandleInteraction(args, secondary: false);
    }

    private bool OnUseSecondary(in PointerInputCmdHandler.PointerInputCmdArgs args)
    {
        return HandleInteraction(args, secondary: true);
    }

    private bool HandleInteraction(in PointerInputCmdHandler.PointerInputCmdArgs args, bool secondary)
    {
        var playerEntity = _playerManager.LocalEntity;
        if (playerEntity == null || !playerEntity.Value.Valid || !EntityManager.HasComponent<FirstPersonComponent>(playerEntity.Value))
            return false;

        var player = playerEntity.Value;

        if (args.State != BoundKeyState.Down)
            return false;

        // Модуль 4 TDD: Рэйкаст по вектору направления камеры
        var xformQuery = GetEntityQuery<TransformComponent>();
        var xform = xformQuery.GetComponent(player);
        var worldRot = _transform.GetWorldRotation(player, xformQuery);
        var worldPos = _transform.GetWorldPosition(player, xformQuery);
        var mapId = xform.MapID;
        
        var dir = worldRot.ToVec();
        var ray = new CollisionRay(worldPos, dir, (int) (CollisionGroup.Impassable | CollisionGroup.InteractImpassable));
        
        var result = _physics.IntersectRay(mapId, ray, 2.0f, player).FirstOrNull();
        if (result != null)
        {
            _interaction.DoContactInteraction(player, result.Value.HitEntity);
            return true;
        }

        return false;
    }

    private void OnFirstPersonInit(EntityUid uid, FirstPersonComponent component, ComponentInit args)
    {
        if (uid != _playerManager.LocalEntity)
            return;

        EnableFirstPerson();
    }

    private void OnFirstPersonShutdown(EntityUid uid, FirstPersonComponent component, ComponentShutdown args)
    {
        if (uid != _playerManager.LocalEntity)
            return;

        DisableFirstPerson();
    }

    private void OnPlayerAttached(EntityUid uid, FirstPersonComponent component, LocalPlayerAttachedEvent args)
    {
        EnableFirstPerson();
    }

    private void OnPlayerDetached(EntityUid uid, FirstPersonComponent component, LocalPlayerDetachedEvent args)
    {
        DisableFirstPerson();
    }

    private void EnableFirstPerson()
    {
        if (!_overlayManager.HasOverlay<FirstPersonOverlay>())
        {
            _overlayManager.AddOverlay(_overlay);
        }
    }

    private void DisableFirstPerson()
    {
        if (_overlayManager.HasOverlay<FirstPersonOverlay>())
        {
            _overlayManager.RemoveOverlay(_overlay);
        }
    }
}
