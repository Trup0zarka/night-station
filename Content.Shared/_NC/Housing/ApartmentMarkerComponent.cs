using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Robust.Shared.Serialization.Manager.Attributes;
using System.Numerics;

namespace Content.Shared._NC.Housing;

/// <summary>
/// Невидимая сервисная энтити, размещаемая на карте в центре будущей квартиры.
/// Хранит всю информацию о владельце, текущем дизайне, заспавненных энтитях
/// и оригинальных тайлах пола (для восстановления при продаже).
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState]
public sealed partial class ApartmentMarkerComponent : Component
{
    /// <summary>
    /// Уникальный строковый идентификатор квартиры (например, "Apt_MegaTower_101").
    /// Задается маппером в YAML.
    /// </summary>
    [DataField("apartmentId", required: true)]
    [AutoNetworkedField]
    public string ApartmentId = string.Empty;

    /// <summary>
    /// Размер зоны квартиры (например, 5x5).
    /// Определяет, какие дизайны доступны, и очерчивает зону для замены пола.
    /// </summary>
    [DataField("size", required: true)]
    [AutoNetworkedField]
    public Vector2i Size = new(5, 5);

    /// <summary>
    /// EntityUid владельца квартиры.
    /// null = квартира свободна и может быть куплена.
    /// </summary>
    [DataField("owner")]
    [AutoNetworkedField]
    public EntityUid? Owner;

    /// <summary>
    /// ID текущего примененного прототипа дизайна (ApartmentDesignPrototype.ID).
    /// null = квартира пуста (базовая коробка).
    /// </summary>
    [DataField("currentDesignId")]
    [AutoNetworkedField]
    public string? CurrentDesignId;

    /// <summary>
    /// Список UID всех энтитей, созданных текущим дизайном.
    /// Используется для очистки при смене дизайна или продаже.
    /// </summary>
    [DataField("spawnedEntities")]
    public List<EntityUid> SpawnedEntities = new();

    /// <summary>
    /// Кэш оригинальных тайлов пола (координата + тайл).
    /// Сохраняется перед заменой пола, восстанавливается при продаже.
    /// </summary>
    [DataField("originalFloorTiles")]
    public List<(Vector2i Pos, Tile OldTile)> OriginalFloorTiles = new();

    /// <summary>
    /// Множитель цены для элитных районов.
    /// Финальная цена = ApartmentDesignPrototype.BasePrice * PriceMultiplier.
    /// </summary>
    [DataField("priceMultiplier")]
    public float PriceMultiplier = 1.0f;

    /// <summary>
    /// Человекочитаемое название квартиры для UI (например, "Квартира 101, МегаТауэр").
    /// </summary>
    [DataField("displayName")]
    [AutoNetworkedField]
    public string DisplayName = "Квартира";
}
