using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared.Audio;

/// <summary>
/// Marking a grid with some ingame music.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class GridMusicComponent : Component
{
    [DataField("musicSoundId"), AutoNetworkedField]
    public string MusicSoundId = "MusicGeneral";
}
