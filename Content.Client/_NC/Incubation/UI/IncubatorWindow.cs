using System;
using System.Numerics; // Обеспечивает поддержку Vector2
using Content.Shared._NC.Incubation;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Maths; // Обеспечивает поддержку Thickness, Color и др.

namespace Content.Client._NC.Incubation.UI
{
    public sealed class IncubatorWindow : DefaultWindow
    {
        public event Action? OnStartPressed;
        public event Action? OnEjectPressed;
        public event Action? OnEmptyPressed;

        // Элементы интерфейса
        private readonly Label _statusLabel;
        private readonly Label _biomassLabel;
        private readonly ProgressBar _biomassProgressBar;
        private readonly Label _progressLabel;
        private readonly ProgressBar _incubationProgressBar;

        private readonly Button _startButton;
        private readonly Button _ejectButton;
        private readonly Button _emptyButton;

        public IncubatorWindow()
        {
            Title = "Управление инкубатором выращивания тел";
            MinSize = new Vector2(400, 300);

            // Главный вертикальный контейнер
            var rootContainer = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(10)
            };

            // Блок статуса устройства
            var statusBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                Margin = new Thickness(0, 0, 0, 10)
            };
            statusBox.AddChild(new Label { Text = "Статус аппарата: ", FontColorOverride = Color.FromHex("#A0A0A0") });
            _statusLabel = new Label { Text = "Простой", FontColorOverride = Color.FromHex("#FFFFFF") };
            statusBox.AddChild(_statusLabel);
            rootContainer.AddChild(statusBox);

            // Блок информации о накопленной биомассе
            var biomassBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(0, 0, 0, 15)
            };
            var biomassHeaderBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            biomassHeaderBox.AddChild(new Label { Text = "Биомасса в баке: ", FontColorOverride = Color.FromHex("#A0A0A0") });
            _biomassLabel = new Label { Text = "0 / 100 ед.", FontColorOverride = Color.FromHex("#00FF00") };
            biomassHeaderBox.AddChild(_biomassLabel);
            biomassBox.AddChild(biomassHeaderBox);

            _biomassProgressBar = new ProgressBar
            {
                MinValue = 0,
                MaxValue = 100,
                Value = 0,
                MinHeight = 15,
                Margin = new Thickness(0, 5, 0, 0)
            };
            biomassBox.AddChild(_biomassProgressBar);
            rootContainer.AddChild(biomassBox);

            // Блок прогресса инкубации
            var progressBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Vertical,
                Margin = new Thickness(0, 0, 0, 20)
            };
            var progressHeaderBox = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Horizontal };
            progressHeaderBox.AddChild(new Label { Text = "Прогресс синтеза: ", FontColorOverride = Color.FromHex("#A0A0A0") });
            _progressLabel = new Label { Text = "Ожидание запуска", FontColorOverride = Color.FromHex("#FFFF00") };
            progressHeaderBox.AddChild(_progressLabel);
            progressBox.AddChild(progressHeaderBox);

            _incubationProgressBar = new ProgressBar
            {
                MinHeight = 20,
                Margin = new Thickness(0, 5, 0, 0)
            };
            progressBox.AddChild(_incubationProgressBar);
            rootContainer.AddChild(progressBox);

            // Блок кнопок управления
            var buttonBox = new BoxContainer
            {
                Orientation = BoxContainer.LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Align = BoxContainer.AlignMode.Center,
                Margin = new Thickness(0, 10, 0, 0)
            };

            _startButton = new Button { Text = "Запустить синтез", Margin = new Thickness(5) };
            _startButton.OnPressed += _ => OnStartPressed?.Invoke();
            buttonBox.AddChild(_startButton);

            _ejectButton = new Button { Text = "Извлечь тело", Margin = new Thickness(5) };
            _ejectButton.OnPressed += _ => OnEjectPressed?.Invoke();
            buttonBox.AddChild(_ejectButton);

            _emptyButton = new Button { Text = "Выгрузить бак", Margin = new Thickness(5) };
            _emptyButton.OnPressed += _ => OnEmptyPressed?.Invoke();
            buttonBox.AddChild(_emptyButton);

            rootContainer.AddChild(buttonBox);

            Contents.AddChild(rootContainer);
        }

        public void UpdateState(IncubatorBoundUserInterfaceState state)
        {
            // Обновляем счетчик биомассы
            _biomassLabel.Text = $"{state.CurrentBiomass} / {state.RequiredBiomass} ед.";
            _biomassProgressBar.MaxValue = state.RequiredBiomass;
            _biomassProgressBar.Value = Math.Min(state.CurrentBiomass, state.RequiredBiomass);

            // Ограничение по питанию
            if (!state.IsPowered)
            {
                _statusLabel.Text = "Питание отсутствует";
                _statusLabel.FontColorOverride = Color.FromHex("#FF0000");

                _startButton.Disabled = true;
                _ejectButton.Disabled = true;
                _emptyButton.Disabled = true;

                _progressLabel.Text = "Аппарат обесточен";
                _incubationProgressBar.Value = 0f;
                return;
            }

            // Логика состояний на основе полученных данных
            if (state.IsGrowing)
            {
                _statusLabel.Text = "Идёт синтез тканей...";
                _statusLabel.FontColorOverride = Color.FromHex("#FF9900");

                _startButton.Disabled = true;
                _ejectButton.Disabled = true;
                _emptyButton.Disabled = true;

                var elapsedTime = state.TotalIncubationTime - state.RemainingTime;
                var percent = state.TotalIncubationTime > 0 ? (elapsedTime / state.TotalIncubationTime) * 100f : 0f;

                _progressLabel.Text = $"Выращивание: {percent:0.0}% ({state.RemainingTime:0.0} сек. осталось)";
                _incubationProgressBar.MaxValue = state.TotalIncubationTime;
                _incubationProgressBar.Value = elapsedTime;
            }
            else if (state.HasBodyInside)
            {
                _statusLabel.Text = "Синтез завершен!";
                _statusLabel.FontColorOverride = Color.FromHex("#00FFFF");

                _startButton.Disabled = true;
                _ejectButton.Disabled = false;
                _emptyButton.Disabled = true;

                _progressLabel.Text = "Тело выращено и ждет извлечения";
                _incubationProgressBar.MaxValue = 100f;
                _incubationProgressBar.Value = 100f;
            }
            else
            {
                _statusLabel.Text = "Простой (Готов к запуску)";
                _statusLabel.FontColorOverride = Color.FromHex("#00FF00");

                _startButton.Disabled = state.CurrentBiomass < state.RequiredBiomass;
                _ejectButton.Disabled = true;
                _emptyButton.Disabled = state.CurrentBiomass <= 0;

                _progressLabel.Text = "Ожидание запуска";
                _incubationProgressBar.Value = 0f;
            }
        }
    }
}