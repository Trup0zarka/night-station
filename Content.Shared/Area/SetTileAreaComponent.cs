namespace Content.Shared.Area;

[RegisterComponent]
public sealed partial class SetTileAreaComponent : Component
{
    [DataField]
    public Color Color { get; set; } = Color.Red;
}
