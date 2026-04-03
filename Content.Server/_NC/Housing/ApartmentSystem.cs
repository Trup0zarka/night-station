using Content.Server._NC.Bank;
using Content.Server.GameTicking;
using Content.Server.Popups;
using Content.Server.Station.Systems;
using Content.Shared._NC.Housing;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Components;
using Robust.Server.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using System.Linq;
using System.Numerics;
using Robust.Shared.EntitySerialization.Systems;

namespace Content.Server._NC.Housing;

/// <summary>
/// Серверная система управления квартирами.
///
/// Жизненный цикл:
/// 1. PostGameMapLoad → создаём скрытую карту, загружаем все префабы дизайнов.
/// 2. Игрок открывает терминал → собираем список квартир/дизайнов, отправляем BUI state.
/// 3. Игрок покупает дизайн → валидация, списание средств, пакетная замена тайлов,
///    тайм-слайсинг клонирование мебели.
/// 4. Игрок продаёт → удаление спавнённых энтитей, восстановление пола, спавн ключа.
/// 5. RoundRestart → очистка скрытой карты.
/// </summary>
public sealed class ApartmentSystem : EntitySystem
{
    // === ЗАВИСИМОСТИ ===
    [Dependency] private readonly IPrototypeManager _protoManager = default!;
    [Dependency] private readonly MapSystem _mapSystem = default!;
    [Dependency] private readonly MapLoaderSystem _mapLoader = default!;
    [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;
    [Dependency] private readonly BankSystem _bankSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly MetaDataSystem _meta = default!;

    private ISawmill _log = default!;

    // === СКРЫТАЯ КАРТА (Storage Map) ===
    /// <summary>
    /// MapId скрытой карты, на которую пре-загружены все дизайны.
    /// </summary>
    private MapId? _storageMapId;

    /// <summary>
    /// EntityUid карты-хранилища (для позиционирования гридов).
    /// </summary>
    private EntityUid _storageMapUid;

    /// <summary>
    /// Кэш: DesignId → EntityUid грида на скрытой карте.
    /// Позволяет копировать энтити и тайлы с этого грида на основную карту.
    /// </summary>
    private readonly Dictionary<string, EntityUid> _preloadedGrids = new();

    // === ТАЙМ-СЛАЙСИНГ ===
    /// <summary>
    /// Максимальное количество энтитей, клонируемых за один серверный тик.
    /// Это значение «размазывает» нагрузку на CPU и предотвращает просадки TPS.
    /// </summary>
    private const int EntitiesPerTick = 15;

    /// <summary>
    /// Очередь задач на клонирование.
    /// Каждая задача содержит данные для постепенного спавна мебели.
    /// </summary>
    private readonly Queue<SpawnJob> _spawnQueue = new();

    public override void Initialize()
    {
        base.Initialize();
        _log = Logger.GetSawmill("housing");

        // Загрузка префабов после загрузки игровой карты
        SubscribeLocalEvent<PostGameMapLoad>(OnPostGameMapLoad);

        // Очистка при рестарте раунда
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        // BUI события
        SubscribeLocalEvent<ApartmentTerminalComponent, BoundUIOpenedEvent>(OnTerminalOpened);
        SubscribeLocalEvent<ApartmentTerminalComponent, ApartmentBuyMessage>(OnBuyMessage);
        SubscribeLocalEvent<ApartmentTerminalComponent, ApartmentSellMessage>(OnSellMessage);
    }

    // ==========================================
    //  ТАЙМ-СЛАЙСИНГ: Update() каждый тик
    // ==========================================

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Обрабатываем тайм-слайсинг очередь
        if (_spawnQueue.Count == 0)
            return;

        var job = _spawnQueue.Peek();
        var spawned = 0;

        // Клонируем по EntitiesPerTick штук за тик
        while (spawned < EntitiesPerTick && job.Index < job.EntitiesToClone.Count)
        {
            var sourceUid = job.EntitiesToClone[job.Index];
            job.Index++;

            // Получаем позицию исходной энтити относительно центра префаба
            var sourceXform = Transform(sourceUid);
            var localPos = sourceXform.LocalPosition;

            // Считаем смещение: позиция на префабе - центр префаба + позиция маркера
            var targetPos = localPos - job.PrefabCenter + job.MarkerWorldPos;

            // Спавним клон через прототип (если он есть)
            var meta = MetaData(sourceUid);
            if (meta.EntityPrototype == null)
            {
                // Энтити без прототипа — пропускаем (декаль, или что-то системное)
                continue;
            }

            var protoId = meta.EntityPrototype.ID;
            var newUid = Spawn(protoId, new EntityCoordinates(job.TargetGridUid, targetPos));

            // Копируем угол поворота (важно для мебели)
            var newXform = Transform(newUid);
            _transform.SetLocalRotation(newUid, sourceXform.LocalRotation);

            // Регистрируем клон в маркере
            job.Marker.SpawnedEntities.Add(newUid);
            Dirty(job.MarkerUid, job.Marker);

            spawned++;
        }

        // Если задача завершена — убираем из очереди
        if (job.Index >= job.EntitiesToClone.Count)
        {
            _spawnQueue.Dequeue();
            _log.Info($"Housing: Спавн завершён для квартиры '{job.Marker.ApartmentId}'. " +
                      $"Создано {job.Marker.SpawnedEntities.Count} энтитей.");
        }
    }

    // ==========================================
    //  ПРЕ-ЛОАД ДИЗАЙНОВ (при старте раунда)
    // ==========================================

    /// <summary>
    /// После загрузки игровой карты создаём скрытую paused-карту
    /// и загружаем на неё все чертежи из ApartmentDesignPrototype.
    /// Таким образом File I/O происходит ОДИН раз до начала раунда.
    /// </summary>
    private void OnPostGameMapLoad(PostGameMapLoad ev)
    {
        // Создаём скрытую карту (paused, чтобы Atmos/Physics не считали)
        _storageMapUid = _mapSystem.CreateMap(out var mapId, runMapInit: false);
        _storageMapId = mapId;
        _meta.SetEntityName(_storageMapUid, "ApartmentDesignStorage");
        _mapSystem.SetPaused(mapId, true);

        _log.Info("Housing: Создана скрытая карта для хранения дизайнов квартир.");

        // Итерируем все прототипы дизайнов
        var globalXOffset = 0f;
        foreach (var proto in _protoManager.EnumeratePrototypes<ApartmentDesignPrototype>())
        {
            if (!_mapLoader.TryLoadGrid(mapId, proto.MapPath, out var grid))
            {
                _log.Error($"Housing: Не удалось загрузить дизайн '{proto.ID}' из '{proto.MapPath}'.");
                continue;
            }

            var (gridUid, mapGrid) = grid.Value;

            // Смещаем грид, чтобы они не накладывались друг на друга
            globalXOffset += mapGrid.LocalAABB.Width / 2;
            var coords = new Vector2(-globalXOffset, 0);
            _transform.SetCoordinates(gridUid, new EntityCoordinates(_storageMapUid, coords));
            globalXOffset += mapGrid.LocalAABB.Width / 2 + 5;

            // Кэшируем UID грида для быстрого доступа при покупке
            _preloadedGrids[proto.ID] = gridUid;

            _log.Info($"Housing: Дизайн '{proto.ID}' ({proto.Name}) загружен на скрытую карту.");
        }

        _log.Info($"Housing: Пре-лоад завершён. Загружено {_preloadedGrids.Count} дизайнов.");
    }

    /// <summary>
    /// Очистка при рестарте раунда. Удаляем скрытую карту и все кэши.
    /// </summary>
    private void OnRoundRestart(RoundRestartCleanupEvent ev)
    {
        if (_storageMapId != null)
        {
            _mapSystem.DeleteMap(_storageMapId.Value);
            _storageMapId = null;
            _log.Info("Housing: Скрытая карта удалена.");
        }

        _preloadedGrids.Clear();
        _spawnQueue.Clear();
    }

    // ==========================================
    //  BUI: ОТКРЫТИЕ ТЕРМИНАЛА
    // ==========================================

    /// <summary>
    /// Когда игрок открывает терминал — собираем данные обо всех квартирах
    /// и отправляем BUI state клиенту.
    /// </summary>
    private void OnTerminalOpened(EntityUid uid, ApartmentTerminalComponent component, BoundUIOpenedEvent args)
    {
        UpdateTerminalUi(uid, args.Actor);
    }

    /// <summary>
    /// Собирает актуальный State и отправляет его клиенту.
    /// Вынесен в отдельный метод, чтобы его можно было вызвать
    /// не только из обработчика BoundUIOpenedEvent.
    /// </summary>
    private void UpdateTerminalUi(EntityUid terminalUid, EntityUid player)
    {
        // Собираем список квартир
        var apartments = new List<ApartmentListingInfo>();
        var query = EntityQueryEnumerator<ApartmentMarkerComponent>();
        while (query.MoveNext(out var markerUid, out var marker))
        {
            apartments.Add(new ApartmentListingInfo
            {
                MarkerNetEntity = GetNetEntity(markerUid),
                DisplayName = marker.DisplayName,
                Size = marker.Size,
                PriceMultiplier = marker.PriceMultiplier,
                IsFree = marker.Owner == null,
                IsOwnedByPlayer = marker.Owner == player,
                CurrentDesignId = marker.CurrentDesignId,
            });
        }

        // Собираем список дизайнов
        var designs = new List<ApartmentDesignInfo>();
        foreach (var proto in _protoManager.EnumeratePrototypes<ApartmentDesignPrototype>())
        {
            designs.Add(new ApartmentDesignInfo
            {
                DesignId = proto.ID,
                Name = proto.Name,
                Description = proto.Description,
                BasePrice = proto.BasePrice,
                RequiredSize = proto.RequiredSize,
            });
        }

        // Получаем баланс игрока
        var balance = _bankSystem.GetBalance(player);

        // Отправляем state
        var state = new ApartmentTerminalBuiState
        {
            PlayerBalance = balance,
            Apartments = apartments,
            Designs = designs,
        };

        _uiSystem.SetUiState(terminalUid, ApartmentTerminalUiKey.Key, state);
    }

    // ==========================================
    //  ПОКУПКА ДИЗАЙНА
    // ==========================================

    /// <summary>
    /// Обработка BUI-сообщения "Купить".
    /// Выполняет полную валидацию и запускает процесс спавна.
    /// </summary>
    private void OnBuyMessage(EntityUid uid, ApartmentTerminalComponent component, ApartmentBuyMessage args)
    {
        var player = args.Actor;

        // 1. Резолвим маркер
        if (!TryGetEntity(args.MarkerNetEntity, out var markerUid) ||
            !TryComp<ApartmentMarkerComponent>(markerUid, out var marker))
        {
            _popupSystem.PopupEntity("Ошибка: квартира не найдена.", uid, player);
            return;
        }

        // 2. Проверяем, что квартира свободна
        if (marker.Owner != null)
        {
            _popupSystem.PopupEntity("Эта квартира уже занята!", uid, player);
            return;
        }

        // 3. Проверяем стакинг (нет ли уже заспавненных объектов)
        if (marker.SpawnedEntities.Count > 0)
        {
            _popupSystem.PopupEntity("Ошибка: квартира находится в процессе обустройства.", uid, player);
            return;
        }

        // 4. Находим прототип дизайна
        if (!_protoManager.TryIndex<ApartmentDesignPrototype>(args.DesignId, out var design))
        {
            _popupSystem.PopupEntity("Ошибка: дизайн не найден.", uid, player);
            return;
        }

        // 5. Проверяем совместимость размера
        if (design.RequiredSize.X > marker.Size.X || design.RequiredSize.Y > marker.Size.Y)
        {
            _popupSystem.PopupEntity("Этот дизайн слишком велик для данной квартиры!", uid, player);
            return;
        }

        // 6. Проверяем, что в зоне квартиры нет живых существ
        if (HasLivingEntitiesInArea(markerUid.Value, marker))
        {
            _popupSystem.PopupEntity("В зоне квартиры находятся люди! Очистите помещение.", uid, player);
            return;
        }

        // 7. Считаем финальную цену и списываем
        var finalPrice = (int)(design.BasePrice * marker.PriceMultiplier);
        if (!_bankSystem.TryBankWithdraw(player, finalPrice))
        {
            _popupSystem.PopupEntity("Недостаточно средств на счету!", uid, player);
            return;
        }

        // 8. Устанавливаем владельца
        marker.Owner = player;
        marker.CurrentDesignId = design.ID;
        Dirty(markerUid.Value, marker);

        // 9. Запускаем процесс спавна дизайна
        StartDesignSpawn(markerUid.Value, marker, design);

        // 10. Спавним физический ключ и выдаём игроку
        SpawnKeyForPlayer(player, marker.ApartmentId);

        _popupSystem.PopupEntity($"Поздравляем! Вы купили '{marker.DisplayName}' за {finalPrice}$.", uid, player);
        _log.Info($"Housing: Игрок {ToPrettyString(player)} купил квартиру '{marker.ApartmentId}' с дизайном '{design.ID}' за {finalPrice}$.");

        // 11. Обновляем UI для всех пользователей терминала
        UpdateTerminalUi(uid, player);
    }

    // ==========================================
    //  ПРОДАЖА / ОСВОБОЖДЕНИЕ
    // ==========================================

    /// <summary>
    /// Обработка BUI-сообщения "Продать".
    /// </summary>
    private void OnSellMessage(EntityUid uid, ApartmentTerminalComponent component, ApartmentSellMessage args)
    {
        var player = args.Actor;

        // 1. Резолвим маркер
        if (!TryGetEntity(args.MarkerNetEntity, out var markerUid) ||
            !TryComp<ApartmentMarkerComponent>(markerUid, out var marker))
        {
            _popupSystem.PopupEntity("Ошибка: квартира не найдена.", uid, player);
            return;
        }

        // 2. Проверяем, что игрок — владелец
        if (marker.Owner != player)
        {
            _popupSystem.PopupEntity("Это не ваша квартира!", uid, player);
            return;
        }

        // 3. Проверяем, что в зоне квартиры нет живых существ
        if (HasLivingEntitiesInArea(markerUid.Value, marker))
        {
            _popupSystem.PopupEntity("В квартире находятся люди! Нельзя продать.", uid, player);
            return;
        }

        // 4. Очищаем квартиру
        ClearApartment(markerUid.Value, marker);

        // 5. Возвращаем часть денег (50% от текущего дизайна)
        if (marker.CurrentDesignId != null &&
            _protoManager.TryIndex<ApartmentDesignPrototype>(marker.CurrentDesignId, out var design))
        {
            var refund = (int)(design.BasePrice * marker.PriceMultiplier * 0.5f);
            if (refund > 0)
            {
                _bankSystem.TryBankDeposit(player, refund);
                _popupSystem.PopupEntity($"Возврат средств: {refund}$.", uid, player);
            }
        }

        // 6. Сбрасываем состояние маркера
        marker.Owner = null;
        marker.CurrentDesignId = null;
        Dirty(markerUid.Value, marker);

        _popupSystem.PopupEntity($"Квартира '{marker.DisplayName}' успешно продана.", uid, player);
        _log.Info($"Housing: Игрок {ToPrettyString(player)} продал квартиру '{marker.ApartmentId}'.");

        // 7. Обновляем UI
        UpdateTerminalUi(uid, player);
    }

    // ==========================================
    //  ЛОГИКА СПАВНА ДИЗАЙНА
    // ==========================================

    /// <summary>
    /// Инициирует пакетную замену тайлов и ставит задачу на тайм-слайсинг клонирование.
    /// </summary>
    private void StartDesignSpawn(EntityUid markerUid, ApartmentMarkerComponent marker, ApartmentDesignPrototype design)
    {
        // 1. Находим пре-загруженный грид дизайна
        if (!_preloadedGrids.TryGetValue(design.ID, out var prefabGridUid))
        {
            _log.Error($"Housing: Дизайн '{design.ID}' не найден в кэше пре-лоада!");
            return;
        }

        if (!TryComp<MapGridComponent>(prefabGridUid, out var prefabGrid))
        {
            _log.Error($"Housing: GridComponent не найден для пре-загруженного дизайна '{design.ID}'.");
            return;
        }

        // 2. Получаем грид основной карты (на котором стоит маркер)
        var markerXform = Transform(markerUid);
        if (markerXform.GridUid == null || !TryComp<MapGridComponent>(markerXform.GridUid, out var stationGrid))
        {
            _log.Error($"Housing: Маркер '{marker.ApartmentId}' не стоит на гриде!");
            return;
        }

        var stationGridUid = markerXform.GridUid.Value;
        // ВАЖНО: используем LocalPosition (координаты относительно грида),
        // а не GetWorldPosition — иначе спавн уедет на offset грида на карте
        var markerLocalPos = markerXform.LocalPosition;

        // 3. Считаем центр префаба
        var prefabCenter = prefabGrid.LocalAABB.Center;

        // 4. Пакетная замена тайлов пола
        ReplaceTiles(markerUid, marker, stationGridUid, stationGrid, prefabGridUid, prefabGrid, markerLocalPos, prefabCenter);

        // 5. Собираем список энтитей на префаб-гриде для клонирования
        var entitiesToClone = new List<EntityUid>();
        var childEnum = Transform(prefabGridUid).ChildEnumerator;
        while (childEnum.MoveNext(out var child))
        {
            // Пропускаем саму сетку и маркеры
            if (HasComp<MapGridComponent>(child) || HasComp<ApartmentMarkerComponent>(child))
                continue;

            entitiesToClone.Add(child);
        }

        // 6. Ставим задачу в очередь тайм-слайсинга
        if (entitiesToClone.Count > 0)
        {
            _spawnQueue.Enqueue(new SpawnJob
            {
                MarkerUid = markerUid,
                Marker = marker,
                TargetGridUid = stationGridUid,
                EntitiesToClone = entitiesToClone,
                PrefabCenter = prefabCenter,
                MarkerWorldPos = markerLocalPos,
                Index = 0,
            });

            _log.Info($"Housing: Начат тайм-слайсинг спавн для '{marker.ApartmentId}'. " +
                      $"Энтитей к клонированию: {entitiesToClone.Count}.");
        }
    }

    /// <summary>
    /// Пакетная замена тайлов пола.
    /// 1. Сохраняет оригинальные тайлы основной карты в маркер.
    /// 2. Читает тайлы с пре-загруженного префаба.
    /// 3. Заменяет тайлы в зоне квартиры одним вызовом SetTiles (без поштучного перерасчета).
    /// </summary>
    private void ReplaceTiles(
        EntityUid markerUid,
        ApartmentMarkerComponent marker,
        EntityUid stationGridUid,
        MapGridComponent stationGrid,
        EntityUid prefabGridUid,
        MapGridComponent prefabGrid,
        Vector2 markerLocalPos,
        Vector2 prefabCenter)
    {
        // Сохраняем оригинальные тайлы
        marker.OriginalFloorTiles.Clear();

        // Собираем тайлы для замены
        var newTiles = new List<(Vector2i GridIndices, Tile Tile)>();

        // Итерируем все тайлы на пре-загруженном префабе
        foreach (var tileRef in _mapSystem.GetAllTiles(prefabGridUid, prefabGrid))
        {
            // Позиция тайла относительно центра префаба → позиция на основной карте
            var relativePos = new Vector2(tileRef.GridIndices.X, tileRef.GridIndices.Y) - prefabCenter;
            var stationTilePos = new Vector2i(
                (int)MathF.Floor(markerLocalPos.X + relativePos.X),
                (int)MathF.Floor(markerLocalPos.Y + relativePos.Y));

            // Сохраняем текущий тайл на этой позиции
            var currentTileRef = _mapSystem.GetTileRef(stationGridUid, stationGrid, stationTilePos);
            marker.OriginalFloorTiles.Add((stationTilePos, currentTileRef.Tile));

            // Добавляем новый тайл в пакет
            newTiles.Add((stationTilePos, tileRef.Tile));
        }

        // Пакетная замена (один вызов — один перерасчет сетки)
        if (newTiles.Count > 0)
        {
            _mapSystem.SetTiles(stationGridUid, stationGrid, newTiles);
            _log.Info($"Housing: Заменено {newTiles.Count} тайлов для '{marker.ApartmentId}'.");
        }

        Dirty(markerUid, marker);
    }

    // ==========================================
    //  ОЧИСТКА КВАРТИРЫ
    // ==========================================

    /// <summary>
    /// Удаляет все заспавненные дизайном энтити и восстанавливает оригинальный пол.
    /// </summary>
    private void ClearApartment(EntityUid markerUid, ApartmentMarkerComponent marker)
    {
        // 1. Удаляем все спавнутые энтити
        foreach (var spawnedUid in marker.SpawnedEntities)
        {
            if (Exists(spawnedUid))
                QueueDel(spawnedUid);
        }
        marker.SpawnedEntities.Clear();

        // 2. Восстанавливаем оригинальный пол
        if (marker.OriginalFloorTiles.Count > 0)
        {
            var markerXform = Transform(markerUid);
            if (markerXform.GridUid != null && TryComp<MapGridComponent>(markerXform.GridUid, out var stationGrid))
            {
                _mapSystem.SetTiles(markerXform.GridUid.Value, stationGrid, marker.OriginalFloorTiles);
                _log.Info($"Housing: Восстановлено {marker.OriginalFloorTiles.Count} оригинальных тайлов для '{marker.ApartmentId}'.");
            }
        }
        marker.OriginalFloorTiles.Clear();

        Dirty(markerUid, marker);
    }

    // ==========================================
    //  УТИЛИТЫ
    // ==========================================

    /// <summary>
    /// Проверяет, есть ли живые существа (MobState) в зоне квартиры.
    /// Используется для защиты от застревания.
    /// </summary>
    private bool HasLivingEntitiesInArea(EntityUid markerUid, ApartmentMarkerComponent marker)
    {
        var markerPos = _transform.GetWorldPosition(markerUid);
        var range = MathF.Max(marker.Size.X, marker.Size.Y) / 2f + 0.5f;

        // Ищем мобов в радиусе квартиры
        var entities = _lookup.GetEntitiesInRange<MobStateComponent>(
            _transform.GetMapCoordinates(markerUid), range);

        return entities.Count > 0;
    }

    /// <summary>
    /// Спавнит физический ключ от квартиры и кладёт его рядом с игроком.
    /// </summary>
    private void SpawnKeyForPlayer(EntityUid player, string apartmentId)
    {
        // Спавним ключ на позиции игрока
        var keyUid = Spawn("ApartmentKey", Transform(player).Coordinates);

        // Устанавливаем метаданные ключа
        _meta.SetEntityName(keyUid, $"Ключ от {apartmentId}");
        _meta.SetEntityDescription(keyUid, $"Физический ключ для доступа в квартиру {apartmentId}.");

        _log.Info($"Housing: Ключ от '{apartmentId}' выдан игроку {ToPrettyString(player)}.");
    }

    // ==========================================
    //  ВНУТРЕННИЙ КЛАСС: Задача на тайм-слайсинг
    // ==========================================

    /// <summary>
    /// Описывает задачу постепенного клонирования энтитей с пре-загруженного дизайна.
    /// </summary>
    private sealed class SpawnJob
    {
        /// <summary>UID маркера квартиры.</summary>
        public EntityUid MarkerUid;

        /// <summary>Ссылка на компонент маркера.</summary>
        public ApartmentMarkerComponent Marker = default!;

        /// <summary>UID грида основной карты (куда клонируем).</summary>
        public EntityUid TargetGridUid;

        /// <summary>Список энтитей на пре-загруженном гриде для клонирования.</summary>
        public List<EntityUid> EntitiesToClone = new();

        /// <summary>Центр пре-загруженного грида (для расчёта оффсета).</summary>
        public Vector2 PrefabCenter;

        /// <summary>Мировая позиция маркера (куда ставим мебель).</summary>
        public Vector2 MarkerWorldPos;

        /// <summary>Текущий индекс в списке (сколько уже заспавнили).</summary>
        public int Index;
    }
}
