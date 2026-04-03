using Robust.Shared.Serialization;
using System.Numerics;

namespace Content.Shared._NC.Housing;

// =============================================
//  BUI Key — ключ для привязки интерфейса терминала
// =============================================

[Serializable, NetSerializable]
public enum ApartmentTerminalUiKey : byte
{
    Key
}

// =============================================
//  Компонент терминала недвижимости
// =============================================

/// <summary>
/// Компонент, размещаемый на стационарном объекте «Терминал недвижимости».
/// При взаимодействии открывает BUI с доступными квартирами и дизайнами.
/// </summary>
[RegisterComponent]
public sealed partial class ApartmentTerminalComponent : Component
{
    // Пока пустой. В будущем можно добавить фильтрацию по зоне, радиус поиска и т.д.
}

// =============================================
//  BUI State — состояние, отправляемое клиенту
// =============================================

/// <summary>
/// Информация об одной квартире для отображения в UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class ApartmentListingInfo
{
    /// <summary>
    /// EntityUid маркера квартиры (для идентификации при покупке).
    /// </summary>
    public NetEntity MarkerNetEntity;

    /// <summary>
    /// Человекочитаемое название (например, "Квартира 101").
    /// </summary>
    public string DisplayName = string.Empty;

    /// <summary>
    /// Размер (например, "5x5").
    /// </summary>
    public Vector2i Size;

    /// <summary>
    /// Множитель цены (для информирования игрока).
    /// </summary>
    public float PriceMultiplier;

    /// <summary>
    /// Свободна ли квартира.
    /// </summary>
    public bool IsFree;

    /// <summary>
    /// Принадлежит ли текущему игроку (для кнопки «Продать»).
    /// </summary>
    public bool IsOwnedByPlayer;

    /// <summary>
    /// ID текущего дизайна (если есть).
    /// </summary>
    public string? CurrentDesignId;
}

/// <summary>
/// Информация о доступном дизайне для отображения в UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class ApartmentDesignInfo
{
    public string DesignId = string.Empty;
    public string Name = string.Empty;
    public string Description = string.Empty;
    public int BasePrice;
    public Vector2i RequiredSize;
}

/// <summary>
/// Состояние BUI терминала недвижимости. Отправляется клиенту при открытии/обновлении.
/// Содержит список доступных квартир и список дизайнов.
/// </summary>
[Serializable, NetSerializable]
public sealed class ApartmentTerminalBuiState : BoundUserInterfaceState
{
    /// <summary>
    /// Баланс текущего игрока (для отображения).
    /// </summary>
    public int PlayerBalance;

    /// <summary>
    /// Список всех квартир (маркеров).
    /// </summary>
    public List<ApartmentListingInfo> Apartments = new();

    /// <summary>
    /// Список всех доступных дизайнов.
    /// </summary>
    public List<ApartmentDesignInfo> Designs = new();
}

// =============================================
//  BUI Messages — сообщения от клиента к серверу
// =============================================

/// <summary>
/// Игрок нажал «Купить».
/// Содержит NetEntity маркера квартиры и ID выбранного дизайна.
/// </summary>
[Serializable, NetSerializable]
public sealed class ApartmentBuyMessage : BoundUserInterfaceMessage
{
    public NetEntity MarkerNetEntity;
    public string DesignId = string.Empty;

    public ApartmentBuyMessage(NetEntity markerNetEntity, string designId)
    {
        MarkerNetEntity = markerNetEntity;
        DesignId = designId;
    }
}

/// <summary>
/// Игрок нажал «Продать / Освободить».
/// </summary>
[Serializable, NetSerializable]
public sealed class ApartmentSellMessage : BoundUserInterfaceMessage
{
    public NetEntity MarkerNetEntity;

    public ApartmentSellMessage(NetEntity markerNetEntity)
    {
        MarkerNetEntity = markerNetEntity;
    }
}
