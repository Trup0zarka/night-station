using Content.Client._NC.FirstPerson.Components;
using Robust.Client.Player;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Content.Shared.Administration;

namespace Content.Client._NC.FirstPerson.Commands;

[AnyCommand]
public sealed class FpsCommand : IConsoleCommand
{
    public string Command => "fps";
    public string Description => "Toggles 2.5D First Person Renderer mode.";
    public string Help => "Usage: fps\nToggles the FirstPersonComponent on your LocalPlayer.";
    public bool RequireServerOrSingleplayer => false;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var playerManager = IoCManager.Resolve<IPlayerManager>();
        var entityManager = IoCManager.Resolve<IEntityManager>();

        var player = playerManager.LocalEntity;
        if (player == null || !player.Value.Valid)
        {
            shell.WriteError("You do not have a valid local player entity.");
            return;
        }

        var uid = player.Value;

        if (entityManager.HasComponent<FirstPersonComponent>(uid))
        {
            entityManager.RemoveComponent<FirstPersonComponent>(uid);
            shell.WriteLine("First Person Renderer OFF.");
        }
        else
        {
            entityManager.AddComponent<FirstPersonComponent>(uid);
            shell.WriteLine("First Person Renderer ON.");
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        return CompletionResult.Empty;
    }

}
