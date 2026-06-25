using Robust.Shared.Serialization;
using Robust.Shared.GameObjects;
using System;

namespace Content.Shared._NC.Incubation
{
    [Serializable, NetSerializable]
    public enum IncubatorUiKey : byte
    {
        Key
    }

    /// <summary>
    /// Состояние интерфейса инкубатора, передаваемое с сервера на клиент.
    /// Не содержит никаких клиентских зависимостей.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class IncubatorBoundUserInterfaceState : BoundUserInterfaceState
    {
        public int CurrentBiomass { get; }
        public int RequiredBiomass { get; }
        public bool IsGrowing { get; }
        public float RemainingTime { get; }
        public float TotalIncubationTime { get; }
        public bool HasBodyInside { get; }
        public bool IsPowered { get; }

        public IncubatorBoundUserInterfaceState(
            int currentBiomass,
            int requiredBiomass,
            bool isGrowing,
            float remainingTime,
            float totalIncubationTime,
            bool hasBodyInside,
            bool isPowered)
        {
            CurrentBiomass = currentBiomass;
            RequiredBiomass = requiredBiomass;
            IsGrowing = isGrowing;
            RemainingTime = remainingTime;
            TotalIncubationTime = totalIncubationTime;
            HasBodyInside = hasBodyInside;
            IsPowered = isPowered;
        }
    }

    /// <summary>
    /// Сообщение, отправляемое от UI клиента серверу при нажатии кнопки.
    /// </summary>
    [Serializable, NetSerializable]
    public sealed class IncubatorUiButtonPressedMessage : BoundUserInterfaceMessage
    {
        public IncubatorUiButton Button { get; }

        public IncubatorUiButtonPressedMessage(IncubatorUiButton button)
        {
            Button = button;
        }
    }

    [Serializable, NetSerializable]
    public enum IncubatorUiButton : byte
    {
        Start,       // Запуск инкубации
        Eject,       // Извлечение готового тела
        EmptyBiomass // Опустошение бака с биомассой
    }
}