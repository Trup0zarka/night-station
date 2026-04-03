using Content.Shared._NC.Vehicle;
using JetBrains.Annotations;
using Robust.Client.GameObjects;

namespace Content.Client._NC.Vehicle.Ui;

[UsedImplicitly]
public sealed class RMCHardpointBoundUserInterface : BoundUserInterface
{
    private RMCHardpointMenu? _menu;

    public RMCHardpointBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        _menu = new RMCHardpointMenu(this);
        _menu.OnClose += Close;
        _menu.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not RMCHardpointBoundUserInterfaceState hardpointState)
            return;

        _menu?.UpdateState(hardpointState);
    }

    public void RemoveHardpoint(string slotId)
    {
        SendMessage(new RMCHardpointRemoveMessage(slotId));
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (!disposing)
            return;

        _menu?.Dispose();
    }
}
