using Content.Client.Area.Overlays;
using Content.Shared.Area;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.GameObjects;

namespace Content.Client.Area;

public sealed class ClientAreaSystem : EntitySystem
{
    [Dependency] private readonly IOverlayManager _overlay = default!;

    private bool _areasVisible;

    public bool AreasVisible
    {
        get => _areasVisible;
        set
        {
            _areasVisible = value;
            UpdateAreas();

            if (value)
                _overlay.AddOverlay(new AreaOverlay());
            else
                _overlay.RemoveOverlay<AreaOverlay>();
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SetTileAreaComponent, ComponentStartup>(OnStartup);
    }

    private void OnStartup(EntityUid uid, SetTileAreaComponent marker, ComponentStartup args)
    {
        UpdateVisibility(uid);
    }

    private void UpdateVisibility(EntityUid uid)
    {
        if (EntityManager.TryGetComponent(uid, out SpriteComponent? sprite))
        {
            sprite.Visible = AreasVisible;
        }
    }

    private void UpdateAreas()
    {
        var query = AllEntityQuery<SetTileAreaComponent>();

        while (query.MoveNext(out var uid, out var comp))
        {
            UpdateVisibility(uid);
        }
    }
}
