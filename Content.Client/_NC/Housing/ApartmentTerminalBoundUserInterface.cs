using Content.Shared._NC.Housing;
using Robust.Client.GameObjects;

namespace Content.Client._NC.Housing;

/// <summary>
/// Клиентский BoundUserInterface для терминала недвижимости.
/// Управляет открытием/закрытием окна и передачей BUI-сообщений серверу.
/// </summary>
public sealed class ApartmentTerminalBoundUserInterface : BoundUserInterface
{
    private ApartmentTerminalWindow? _window;

    public ApartmentTerminalBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = new ApartmentTerminalWindow();

        // Подписываемся на события из UI
        _window.OnBuyPressed += (markerNet, designId) =>
            SendMessage(new ApartmentBuyMessage(markerNet, designId));

        _window.OnSellPressed += markerNet =>
            SendMessage(new ApartmentSellMessage(markerNet));

        _window.OpenCentered();
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not ApartmentTerminalBuiState castState)
            return;

        _window?.UpdateState(castState);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _window?.Dispose();
    }
}
