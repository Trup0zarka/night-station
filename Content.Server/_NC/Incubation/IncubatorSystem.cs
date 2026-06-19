
/// блять а где...


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
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Enums;
using Robust.Shared.Utility;

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
        [Dependency] private readonly MarkingManager _markingManager = default!; // Внедряем менеджер маркировок

        public override void Initialize()
        {
            base.Initialize();

            SubscribeLocalEvent<IncubatorComponent, ComponentInit>(OnComponentInit);
            SubscribeLocalEvent<IncubatorComponent, InteractUsingEvent>(OnInteractUsing);
            SubscribeLocalEvent<IncubatorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAltVerbs);
        }

        private void OnComponentInit(EntityUid uid, IncubatorComponent component, ComponentInit args)
        {
            component.BodyContainer = _containerSystem.EnsureContainer<ContainerSlot>(uid, "incubator-bodyContainer");
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<IncubatorComponent>();
            while (query.MoveNext(out var uid, out var incubator))
            {
                if (incubator.Status != IncubatorStatus.Growing)
                    continue;

                if (!_powerReceiver.IsPowered(uid))
                {
                    _popupSystem.PopupEntity("Питание прервано! Процесс инкубации сброшен.", uid, PopupType.MediumCaution);
                    incubator.Status = IncubatorStatus.Idle;
                    incubator.RemainingTime = 0f;
                    continue;
                }

                incubator.RemainingTime -= frameTime;
                if (incubator.RemainingTime <= 0f)
                {
                    FinishIncubation(uid, incubator);
                }
            }
        }

        private void OnInteractUsing(EntityUid uid, IncubatorComponent component, InteractUsingEvent args)
        {
            if (args.Handled)
                return;

            if (component.Status != IncubatorStatus.Idle)
            {
                _popupSystem.PopupEntity("Инкубатор в данный момент занят процессом инкубации!", uid, args.User);
                return;
            }

            if (_materialStorage.TryInsertMaterialEntity(args.User, args.Used, uid))
            {
                _popupSystem.PopupEntity("Вы загрузили биоматериал в приёмник инкубатора.", uid, args.User);
                args.Handled = true;

                CheckAndTryStart(uid, component);
            }
        }

        private void CheckAndTryStart(EntityUid uid, IncubatorComponent component)
        {
            var currentBiomass = _materialStorage.GetMaterialAmount(uid, component.BiomassMaterial);
            if (currentBiomass < component.RequiredBiomass)
                return;

            // Списываем необходимый объём
            _materialStorage.TryChangeMaterialAmount(uid, component.BiomassMaterial, -component.RequiredBiomass);

            component.Status = IncubatorStatus.Growing;
            component.RemainingTime = component.IncubationTime;

            _popupSystem.PopupEntity("Инкубатор начинает синтезировать ткани. Процесс выращивания запущен.", uid, PopupType.Medium);
        }

        private void FinishIncubation(EntityUid uid, IncubatorComponent component)
        {
            component.RemainingTime = 0f;

            // Спавним тело
            var mob = Spawn(component.SpawnMobPrototype, Transform(uid).Coordinates);
            
            if (TryComp<HumanoidAppearanceComponent>(mob, out var humanoid))
            {
                GenerateRandomAppearance(mob, humanoid);
            }

            // Шанс в 20%
            if (_random.Prob(component.GhostRoleChance))
            {
                var ghostRole = EnsureComp<GhostRoleComponent>(mob);
                ghostRole.RoleName = "Выращенный гомункул";
                ghostRole.RoleDescription = "Искусственно выращенное тело человека, внутри которого неожиданно зародилось сознание.";
                ghostRole.RoleRules = "Вы — искусственно выращенная оболочка. У вас нет прошлого, воспоминаний или документов. Вы можете начать свою историю с чистого листа или следовать указаниям тех, кто вас вырастил.";
            }

            _containerSystem.Insert(mob, component.BodyContainer);

            component.Status = IncubatorStatus.Finished;
            _popupSystem.PopupEntity("Выращивание тела завершено! Готово к извлечению.", uid, PopupType.Medium);

            if (component.AutoEject)
            {
                EjectBody(uid, component);
            }
        }

        private void GenerateRandomAppearance(EntityUid mob, HumanoidAppearanceComponent appearance)
        {
            if (!_prototypeManager.TryIndex<SpeciesPrototype>(appearance.Species, out var speciesProto))
                return;

            // 1. биологический пол
            var sexList = speciesProto.Sexes;
            var chosenSex = _random.Pick(sexList);

            var hairMarkings = _markingManager.MarkingsByCategoryAndSpeciesAndSex(
                MarkingCategories.Hair, 
                appearance.Species, 
                chosenSex
            );
            var chosenHair = hairMarkings.Count > 0 ? _random.Pick(hairMarkings.Keys.ToList()) : "HairStyleNone";

            // фрактикал хейр без бородатых женщин
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

        public void EjectBody(EntityUid uid, IncubatorComponent? component = null)
        {
            if (!Resolve(uid, ref component))
                return;

            if (component.BodyContainer.ContainedEntity is not { } body)
                return;

            _containerSystem.Remove(body, component.BodyContainer);

            component.Status = IncubatorStatus.Idle;
            _popupSystem.PopupEntity("Тело успешно извлечено из инкубационной камеры.", uid, PopupType.Medium);
            
            CheckAndTryStart(uid, component);
        }

        private void OnGetAltVerbs(EntityUid uid, IncubatorComponent component, GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract)
                return;

            if (component.Status == IncubatorStatus.Finished && component.BodyContainer.ContainedEntity != null)
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