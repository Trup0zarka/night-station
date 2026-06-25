using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.ViewVariables;

namespace Content.Shared._NC.Incubation
{
    [RegisterComponent]
    public sealed partial class IncubatorComponent : Component
    {
        /// <summary>
        /// ID контейнера, в котором хранится выращиваемое тело.
        /// </summary>
        [ViewVariables]
        public ContainerSlot BodyContainer = default!;

        /// <summary>
        /// Прототип существа для спавна (по умолчанию базовый человек).
        /// </summary>
        [DataField("spawnMobPrototype")]
        public string SpawnMobPrototype = "MobHuman";

        /// <summary>
        /// Прототип выдаваемой биомассы в виде предмета при её выгрузке.
        /// </summary>
        [DataField("biomassItemPrototype")]
        public string BiomassItemPrototype = "MaterialBiomass";

        /// <summary>
        /// Какое количество материала требуется для запуска инкубации.
        /// </summary>
        [DataField("requiredBiomass")]
        public int RequiredBiomass = 100;

        /// <summary>
        /// ID материала, используемого в качестве биомассы.
        /// </summary>
        [DataField("biomassMaterial")]
        public string BiomassMaterial = "Biomass";

        /// <summary>
        /// Время выращивания тела в секундах.
        /// </summary>
        [DataField("incubationTime")]
        public float IncubationTime = 30f;

        /// <summary>
        /// Оставшееся время до завершения выращивания.
        /// Если больше 0 — процесс инкубации активен.
        /// </summary>
        [ViewVariables]
        public float RemainingTime = 0f;

        /// <summary>
        /// Будет ли инкубатор автоматически выбрасывать тело по окончании процесса.
        /// </summary>
        [DataField("autoEject")]
        public bool AutoEject = false;

        /// <summary>
        /// Шанс того, что выращенное тело станет гост-ролью (от 0.0 до 1.0).
        /// </summary>
        [DataField("ghostRoleChance")]
        public float GhostRoleChance = 0.20f;
    }
}