using Content.Client._NC.FirstPerson.Components;
using Content.Client._NC.FirstPerson.Renderers;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using System.Numerics;
using Vector3 = System.Numerics.Vector3;

namespace Content.Client._NC.FirstPerson.Overlays;

public sealed partial class FirstPersonOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;

    private readonly SharedTransformSystem _transformSystem;
    private readonly FirstPersonGeometryRenderer _geometryRenderer;
    private readonly FirstPersonSpriteRenderer _spriteRenderer;

    private float[] _zBuffer = [];

    // Используем ScreenSpace для Raycasting (отрисовка прямо на экран)
    public override OverlaySpace Space => OverlaySpace.ScreenSpace;

    public FirstPersonOverlay()
    {
        IoCManager.InjectDependencies(this);
        _transformSystem = _entityManager.System<SharedTransformSystem>();
        _geometryRenderer = new FirstPersonGeometryRenderer();
        _spriteRenderer = new FirstPersonSpriteRenderer();
    }

    protected override bool BeforeDraw(in OverlayDrawArgs args)
    {
        if (_playerManager.LocalEntity is not { Valid: true } player
            || !_entityManager.HasComponent<FirstPersonComponent>(player))
            return false;

        return base.BeforeDraw(in args);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var player = _playerManager.LocalEntity!.Value;
        if (!_entityManager.TryGetComponent<FirstPersonComponent>(player, out var fpComp))
            return;

        var screenHandle = args.ScreenHandle;
        var screenSize = new Vector2(args.Viewport.Size.X, args.Viewport.Size.Y);

        // Обновляем размер Z-буфера при изменении окна
        int screenWidth = (int)screenSize.X;
        if (_zBuffer.Length != screenWidth)
        {
            _zBuffer = new float[screenWidth];
        }

        // --- ЭТАП 1: Заливка фона (Потолок и Пол) ---
        var width = screenSize.X;
        var height = screenSize.Y;
        var halfHeight = height / 2f;

        // Потолок (темно-серый)
        screenHandle.DrawRect(new UIBox2(0, 0, width, halfHeight), new Color(40, 40, 40));
        // Пол (серый)
        screenHandle.DrawRect(new UIBox2(0, halfHeight, width, height), new Color(60, 60, 60));

        // Очистка Z-буфера (бесконечность)
        for (var i = 0; i < _zBuffer.Length; i++)
        {
            _zBuffer[i] = float.MaxValue;
        }

        var pos = _transformSystem.GetMapCoordinates(player);
        var rotation = _transformSystem.GetWorldRotation(player);

        // Будущие вызовы
        _geometryRenderer.RenderDDA(screenHandle, pos, rotation, fpComp.Fov, screenSize, _zBuffer, stepPixel: 2);
        _spriteRenderer.RenderSprites(screenHandle, pos, rotation, fpComp.Fov, screenSize, _zBuffer);
    }
}
