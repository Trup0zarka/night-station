using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Numerics;

namespace Content.Shared._NC.Housing;

/// <summary>
/// Прототип дизайна квартиры. Описывает вариант интерьера,
/// который может быть загружен в пустую комнату на станции.
/// Маппер создает .yml файл с мебелью, полом и декором (БЕЗ стен!),
/// а этот прототип связывает его с ценой, размером и описанием.
/// </summary>
[Prototype("apartmentDesign")]
public sealed class ApartmentDesignPrototype : IPrototype
{
    /// <summary>
    /// Уникальный ID прототипа дизайна (например, "Design_Cyberpunk_Neon").
    /// </summary>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Человекочитаемое название дизайна (например, "Неоновый притон").
    /// </summary>
    [DataField("name", required: true)]
    public string Name = string.Empty;

    /// <summary>
    /// Описание дизайна для UI (например, "Минималистично, грязно, много розового света.").
    /// </summary>
    [DataField("description")]
    public string Description = string.Empty;

    /// <summary>
    /// Базовая стоимость дизайна в кредитах.
    /// Финальная цена = BasePrice * ApartmentMarkerComponent.PriceMultiplier.
    /// </summary>
    [DataField("basePrice", required: true)]
    public int BasePrice;

    /// <summary>
    /// Путь к .yml файлу чертежа (грида) с мебелью и полом.
    /// Пример: "/Maps/Prefabs/Apts/neon_5x5.yml"
    /// </summary>
    [DataField("mapPath", required: true)]
    public ResPath MapPath = default!;

    /// <summary>
    /// Минимальный размер квартиры (вектор), необходимый для этого дизайна.
    /// Дизайн с RequiredSize (5,5) не будет доступен для маркера с Size (3,3).
    /// </summary>
    [DataField("requiredSize", required: true)]
    public Vector2i RequiredSize;
}
