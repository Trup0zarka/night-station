using System.Numerics;
using Content.Shared.Area;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Client.Player;
using Robust.Shared.Enums;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Client.Area.Overlays;

public sealed class AreaOverlay : Overlay
{
    [Dependency] private readonly IEntityManager _entityManager = default!;
    [Dependency] private readonly IPlayerManager _player = default!;

    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    public AreaOverlay()
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Draw(in OverlayDrawArgs args)
    {
        var handle = args.WorldHandle;
        var xformSystem = EntitySystem.Get<TransformSystem>();

        var playerUid = _player.LocalEntity;

        if (playerUid is null)
            return;

        if (!_entityManager.TryGetComponent<TransformComponent>(playerUid.Value, out var xform) || xform is null)
            return;

        var grid = xformSystem.GetGrid(playerUid.Value);
        if (grid is null ||
            !_entityManager.TryGetComponent<MapGridComponent>(grid, out var gridComp))
            return;

        if (!_entityManager.TryGetComponent<AreaComponent>(grid.Value, out var areaZones))
            return;

        // Get grid's transform to handle rotation/offset
        var gridXform = xformSystem.GetWorldMatrix(grid.Value);
        handle.SetTransform(gridXform);

        foreach (var (_, zoneData) in areaZones.Data)
        {
            foreach (var tile in zoneData.Tiles)
            {
                // Render in grid-local coordinates
                var position = gridComp.GridTileToLocal(tile);
                var box = new Box2(
                        position.Position - (gridComp.TileSizeVector / 2),
                        (position.Position + gridComp.TileSizeVector) - (gridComp.TileSizeVector / 2));
                handle.DrawRect(box, zoneData.Color.WithAlpha(0.5f));
            }
        }
    }
}
