using Content.Client.UserInterface.Fragments;
using Content.Shared._NC.CitiNet;
using Content.Shared._NC.CitiNet.Components;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;
using Robust.Shared.Prototypes;

namespace Content.Client._NC.CitiNet.UI;

public sealed class NetBrowserBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    private NetBrowserWindow? _window;
    private UIFragment? _activeSiteUI;
    private Control? _activeUiFragment;
    private string? _activeUrl;

    public NetBrowserBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        IoCManager.InjectDependencies(this);
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindowCenteredLeft<NetBrowserWindow>();
        _window.OnNavigate += (url) => SendMessage(new NetBrowserNavigateMessage(url));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not NetBrowserUiState browserState)
        {
            _activeSiteUI?.UpdateState(state);
            return;
        }

        _window?.UpdateState(browserState);

        if (_activeUrl == browserState.CurrentUrl)
        {
            _activeSiteUI?.UpdateState(state);
            return;
        }

        _activeUrl = browserState.CurrentUrl;

        // Find the site prototype for the current URL
        NetSitePrototype? currentSite = null;
        foreach (var site in _prototypeManager.EnumeratePrototypes<NetSitePrototype>())
        {
            if (site.URL == browserState.CurrentUrl)
            {
                currentSite = site;
                break;
            }
        }

        if (currentSite == null)
        {
            DetachSiteUI();
            return;
        }

        var ui = GetUIFragment(currentSite.UiKey);
        if (ui == null)
        {
            DetachSiteUI();
            return;
        }
        
        // Setup before GetUIFragmentRoot to ensure it's initialized
        ui.Setup(this, Owner);
        var control = ui.GetUIFragmentRoot();

        if (control == null)
        {
            DetachSiteUI();
            return;
        }

        if (_activeUiFragment?.GetType() == control.GetType())
        {
            ui.UpdateState(state);
            return;
        }

        DetachSiteUI();
        AttachSiteUI(ui, control);
    }

    private void AttachSiteUI(UIFragment ui, Control control)
    {
        _activeSiteUI = ui;
        _activeUiFragment = control;
        _window?.Viewport.AddChild(control);
    }

    private void DetachSiteUI()
    {
        if (_activeUiFragment != null)
        {
            if (_window is { Disposed: false })
                _window.Viewport.RemoveChild(_activeUiFragment);
            
            _activeUiFragment = null;
        }
        _activeSiteUI = null;
    }

    protected override void ReceiveMessage(BoundUserInterfaceMessage message)
    {
        base.ReceiveMessage(message);
        // This is where we would handle messages FROM the server TO the UI fragment if needed,
        // but normally UpdateState handles that.
    }

    private UIFragment? GetUIFragment(string uiKey)
    {
        return uiKey switch
        {
            "NetHome" => new NetHomeSiteUIFragment(),
            "CitiNetComm" => new CitiNetUi(),
            _ => null
        };
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            DetachSiteUI();
            _window?.Dispose();
            _window = null;
        }
    }
}
