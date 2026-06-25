using System;
using System.Linq;
using Content.Shared._NC.Incubation;
using Content.Shared.Materials;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared.Humanoid.Markings;
using Content.Server.Materials;
using Content.Server.Humanoid;
using Content.Server.Power.EntitySystems;
using Content.Server.Popups;
using Content.Server.Ghost.Roles.Components;
using Robust.Server.Containers;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Enums;
using Robust.Shared.Utility;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;

namespace Content.Server._NC.Incubation
{
    public sealed class IncubatorSystem : EntitySystem
    {
        [Dependency] private readonly ContainerSystem _containerSystem = default!;
        [Dependency] private readonly MaterialStorageSystem _materialStorage = default!;
        [Dependency] private readonly HumanoidAppearanceSystem _humanoidSystem = default!;
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly PowerReceiverSystem _powerReceiver = default!;
        [Dependency] private readonly MarkingManager _markingManager = default!;
        [Dependency] private readonly UserInterfaceSystem _uiSystem = default!;

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<IncubatorComponent, ComponentInit>(OnComponentInit);
            SubscribeLocalEvent<IncubatorComponent, InteractUsingEvent>(OnInteractUsing);
            SubscribeLocalEvent<IncubatorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);
            SubscribeLocalEvent<IncubatorComponent, ActivateInWorldEvent>(OnActivate);
            
            // Обработка сообщений из UI
            SubscribeLocalEvent<IncubatorComponent, IncubatorUiButtonPressedMessage>(OnUiButtonPressed);
        }

        private void OnComponentInit(EntityUid uid, IncubatorComponent component, ComponentInit args)
        {
            component.BodyContainer = _containerSystem.EnsureContainer<ContainerSlot>(uid, "incubator-bodyContainer");
        }

        private void OnActivate(EntityUid uid, IncubatorComponent component, ActivateInWorldEvent args)
        {
            if (args.Handled) 
                return;

            // Передаем args.User (EntityUid) напрямую в TryOpen. Это избавляет от необходимости искать ActorComponent
            if (_uiSystem.TryToggleUi(uid, IncubatorUiKey.Key, args.User))
            {
                UpdateUserInterface(uid, component);
                args.Handled = true;
            }
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<IncubatorComponent>();
            while (query.MoveNext(out var uid, out var incubator))
            {
                if (incubator.RemainingTime > 0f)
                {
                    // Если инкубатор обесточен — прерываем синтез
                    if (!_powerReceiver.IsPowered(uid))
                    {
                        _popupSystem.PopupEntity("Питание прервано! Процесс инкубации сброшен.", uid, PopupType.MediumCaution);
                        incubator.RemainingTime = 0f;
                        UpdateUserInterface(uid, incubator);
                        continue;
                    }

                    incubator.RemainingTime -= frameTime;
                    if (incubator.RemainingTime <= 0f)
                    {
                        FinishIncubation(uid, incubator);
                    }
                    else
                    {
                        UpdateUserInterface(uid, incubator);
                    }
                }
            }
        }

        /// <summary>
        /// Отправка состояния инкубатора в графический интерфейс.
        /// </summary>
        private void UpdateUserInterface(EntityUid uid, IncubatorComponent component)
        {
            var biomassAmount = _materialStorage.GetMaterialAmount(uid, component.BiomassMaterial);
            var isPowered = _powerReceiver.IsPowered(uid);
            var isGrowing = component.RemainingTime > 0f;
            var hasBodyInside = component.BodyContainer.ContainedEntity != null;

            var state = new IncubatorBoundUserInterfaceState(
                (int) biomassAmount,
                component.RequiredBiomass,
                isGrowing,
                component.RemainingTime,
                component.IncubationTime,
                hasBodyInside,
                isPowered
            );

            _uiSystem.SetUiState(uid, IncubatorUiKey.Key, state);
        }

        /// <summary>
        /// Обработка вставки материалов (биомассы) в инкубатор.
        /// </summary>
        private void OnInteractUsing(EntityUid uid, IncubatorComponent component, InteractUsingEvent args)
        {
            if (args.Handled)
                return;

            // Если уже кто-то растёт или готов
            var isGrowing = component.RemainingTime > 0f;
            var hasBodyInside = component.BodyContainer.ContainedEntity != null;
            if (isGrowing || hasBodyInside)
            {
                _popupSystem.PopupEntity("Инкубатор в данный момент занят процессом инкубации!", uid, args.User);
                return;
            }

            // Попытка забрать биоматериал через систему материалов
            if (_materialStorage.TryInsertMaterialEntity(args.User, args.Used, uid))
            {
                _popupSystem.PopupEntity("Вы загрузили биоматериал в приёмник инкубатора.", uid, args.User);
                args.Handled = true;
                UpdateUserInterface(uid, component);
            }
        }

        /// <summary>
        /// Обработка кнопок интерфейса.
        /// </summary>
        private void OnUiButtonPressed(EntityUid uid, IncubatorComponent component, IncubatorUiButtonPressedMessage args)
        {
            if (!_powerReceiver.IsPowered(uid))
                return;

            switch (args.Button)
            {
                case IncubatorUiButton.Start:
                    TryStartIncubation(uid, component);
                    break;
                case IncubatorUiButton.Eject:
                    EjectBody(uid, component);
                    break;
                case IncubatorUiButton.EmptyBiomass:
                    EmptyBiomassStorage(uid, component);
                    break;
            }

            UpdateUserInterface(uid, component);
        }

        /// <summary>
        /// Попытка запустить процесс инкубации вручную через кнопку в UI.
        /// </summary>
        private void TryStartIncubation(EntityUid uid, IncubatorComponent component)
        {
            var isGrowing = component.RemainingTime > 0f;
            var hasBodyInside = component.BodyContainer.ContainedEntity != null;
            if (isGrowing || hasBodyInside)
                return;

            var currentBiomass = _materialStorage.GetMaterialAmount(uid, component.BiomassMaterial);
            if (currentBiomass < component.RequiredBiomass)
            {
                _popupSystem.PopupEntity("Недостаточно биоматериала для запуска синтеза!", uid, PopupType.SmallCaution);
                return;
            }

            // Списываем необходимый объём
            _materialStorage.TryChangeMaterialAmount(uid, component.BiomassMaterial, -component.RequiredBiomass);

            component.RemainingTime = component.IncubationTime;

            _popupSystem.PopupEntity("Инкубатор начинает синтезировать ткани. Процесс выращивания запущен.", uid, PopupType.Medium);
        }

        /// <summary>
        /// Окончание процесса инкубации и генерация тела гуманоида.
        /// </summary>
        private void FinishIncubation(EntityUid uid, IncubatorComponent component)
        {
            component.RemainingTime = 0f;

            // Спавним тело
            var mob = Spawn(component.SpawnMobPrototype, Transform(uid).Coordinates);
            
            if (TryComp<HumanoidAppearanceComponent>(mob, out var humanoid))
            {
                GenerateRandomAppearance(mob, humanoid);
            }

            // Шанс на появление гост-роли
            if (_random.Prob(component.GhostRoleChance))
            {
                var ghostRole = EnsureComp<GhostRoleComponent>(mob);
                ghostRole.RoleName = "Выращенный гомункул";
                ghostRole.RoleDescription = "Искусственно выращенное тело человека, внутри которого зародилось сознание.";
                ghostRole.RoleRules = "Вы — искусственная оболочка. У вас нет прошлого. Вы можете начать свою историю с чистого листа.";
            }

            // Помещаем тело внутрь контейнера инкубатора
            _containerSystem.Insert(mob, component.BodyContainer);

            _popupSystem.PopupEntity("Выращивание тела завершено! Готово к извлечению.", uid, PopupType.Medium);

            UpdateUserInterface(uid, component);

            if (component.AutoEject)
            {
                EjectBody(uid, component);
            }
        }

        /// <summary>
        /// Генерация случайной внешности гуманоида.
        /// </summary>
        private void GenerateRandomAppearance(EntityUid mob, HumanoidAppearanceComponent appearance)
        {
            if (!_prototypeManager.TryIndex<SpeciesPrototype>(appearance.Species, out var speciesProto))
                return;

            var sexList = speciesProto.Sexes;
            var chosenSex = _random.Pick(sexList);

            var hairMarkings = _markingManager.MarkingsByCategoryAndSpeciesAndSex(
                MarkingCategories.Hair, 
                appearance.Species, 
                chosenSex
            );
            var chosenHair = hairMarkings.Count > 0 ? _random.Pick(hairMarkings.Keys.ToList()) : "HairStyleNone";

            string chosenFacialHair = "FacialHairStyleNone";
            if (chosenSex != Sex.Female)
            {
                var facialHairMarkings = _markingManager.MarkingsByCategoryAndSpeciesAndSex(
                    MarkingCategories.FacialHair, 
                    appearance.Species, 
                    chosenSex
                );
                chosenFacialHair = facialHairMarkings.Count > 0 ? _random.Pick(facialHairMarkings.Keys.ToList()) : "FacialHairStyleNone";
            }

            var profile = HumanoidCharacterProfile.RandomWithSpecies(appearance.Species)
                .WithSex(chosenSex)
                .WithGender(chosenSex == Sex.Female ? Gender.Female : Gender.Male);

            var updatedAppearance = profile.Appearance
                .WithHairStyleName(chosenHair)
                .WithFacialHairStyleName(chosenFacialHair);

            profile = profile.WithCharacterAppearance(updatedAppearance);

            _humanoidSystem.LoadProfile(mob, profile);
        }

        /// <summary>
        /// Извлечение готового тела из инкубатора.
        /// </summary>
        public void EjectBody(EntityUid uid, IncubatorComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            if (component.BodyContainer.ContainedEntity is not { } body)
                return;

            _containerSystem.Remove(body, component.BodyContainer);
            
            _popupSystem.PopupEntity("Тело успешно извлечено из инкубационной камеры.", uid, PopupType.Medium);
            
            UpdateUserInterface(uid, component);
        }

        /// <summary>
        /// Выгрузка накопленной в баке биомассы в виде физических предметов наружу.
        /// </summary>
        private void EmptyBiomassStorage(EntityUid uid, IncubatorComponent component)
        {
            var isGrowing = component.RemainingTime > 0f;
            var hasBodyInside = component.BodyContainer.ContainedEntity != null;
            if (isGrowing || hasBodyInside)
            {
                _popupSystem.PopupEntity("Нельзя выгрузить биомассу во время работы!", uid, PopupType.SmallCaution);
                return;
            }

            var currentBiomass = _materialStorage.GetMaterialAmount(uid, component.BiomassMaterial);
            if (currentBiomass <= 0)
            {
                _popupSystem.PopupEntity("Резервуары биомассы пусты!", uid, PopupType.Small);
                return;
            }

            const int unitsPerItem = 50;
            var spawnCount = (int) (currentBiomass / unitsPerItem);
            var remainingBiomass = currentBiomass % unitsPerItem;

            var spawnCoords = Transform(uid).Coordinates;

            for (int i = 0; i < spawnCount; i++)
            {
                Spawn(component.BiomassItemPrototype, spawnCoords);
            }

            _materialStorage.TryChangeMaterialAmount(uid, component.BiomassMaterial, -currentBiomass + remainingBiomass);

            _popupSystem.PopupEntity("Биомасса выгружена на пол.", uid, PopupType.Medium);
            UpdateUserInterface(uid, component);
        }

        private void OnGetAltVerbs(EntityUid uid, IncubatorComponent component, GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract)
                return;

            if (component.BodyContainer.ContainedEntity != null)
            {
                AlternativeVerb ejectVerb = new()
                {
                    Act = () => EjectBody(uid, component),
                    Text = "Извлечь выращенное тело",
                    Icon = new SpriteSpecifier.Texture(new ("/Textures/Interface/VerbIcons/eject.svg.192dpi.png")),
                    Priority = 1
                };
                args.Verbs.Add(ejectVerb);
            }
        }
    }
}