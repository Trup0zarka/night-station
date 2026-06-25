using System;
using Content.Shared._NC.Incubation;
using JetBrains.Annotations;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;

namespace Content.Client._NC.Incubation.UI
{
    [UsedImplicitly]
    public sealed class IncubatorBoundUserInterface : BoundUserInterface
    {
        private IncubatorWindow? _window;

        public IncubatorBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
        {
        }

        protected override void Open()
        {
            base.Open();

            _window = new IncubatorWindow();
            _window.OnClose += Close;

            _window.OnStartPressed += () => SendMessage(new IncubatorUiButtonPressedMessage(IncubatorUiButton.Start));
            _window.OnEjectPressed += () => SendMessage(new IncubatorUiButtonPressedMessage(IncubatorUiButton.Eject));
            _window.OnEmptyPressed += () => SendMessage(new IncubatorUiButtonPressedMessage(IncubatorUiButton.EmptyBiomass));

            _window.OpenToLeft();
        }

        protected override void UpdateState(BoundUserInterfaceState state)
        {
            base.UpdateState(state);

            if (state is not IncubatorBoundUserInterfaceState castState)
                return;

            _window?.UpdateState(castState);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                _window?.Dispose();
            }
        }
    }
}