using Robust.Shared.GameObjects;

namespace Content.Client._NC.FirstPerson.Components;

/// <summary>
/// Маркерный компонент для активации режима от первого лица.
/// </summary>
[RegisterComponent]
public sealed partial class FirstPersonComponent : Component
{
    /// <summary>
    /// Высота глаз игрока.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float EyeHeight { get; set; } = 0.5f;

    /// <summary>
    /// Угол обзора в градусах.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public float Fov { get; set; } = 90f;
}
