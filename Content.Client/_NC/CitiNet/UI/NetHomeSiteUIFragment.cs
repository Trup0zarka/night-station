using Content.Client.UserInterface.Fragments;
using Robust.Client.UserInterface;

namespace Content.Client._NC.CitiNet.UI;

public sealed partial class NetHomeSiteUIFragment : UIFragment
{
    private NetHomeSiteUI? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new NetHomeSiteUI();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
    }
}
