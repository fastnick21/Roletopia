using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;

namespace Roletopia.AmongUsPlugin;

internal static class DynamicPatchInstaller
{
    private sealed record PatchTarget(string TypeName, string MethodName, string BridgeMethodName);

    private static readonly PatchTarget[] Targets =
    {
        new("AmongUsClient", "OnGameJoined", nameof(RoletopiaGameBridge.LobbyCreated)),
        new("GameStartManager", "BeginGame", nameof(RoletopiaGameBridge.GameStarting)),
        new("MeetingHud", "Start", nameof(RoletopiaGameBridge.MeetingStarted)),
        new("MeetingHud", "Close", nameof(RoletopiaGameBridge.MeetingEnded)),
        new("PlayerControl", "CompleteTask", nameof(RoletopiaGameBridge.TaskCompleted)),
        new("EndGameManager", "Start", nameof(RoletopiaGameBridge.GameEnded)),
        new("MainMenuManager", "Start", nameof(RoletopiaGameBridge.MainMenuStarted))
    };

    public static int Install(Harmony harmony, ManualLogSource log)
    {
        ArgumentNullException.ThrowIfNull(harmony);
        ArgumentNullException.ThrowIfNull(log);

        var installed = 0;
        foreach (var target in Targets)
        {
            var gameType = AccessTools.TypeByName(target.TypeName);
            if (gameType == null)
            {
                log.LogWarning($"Patch skipped: type {target.TypeName} was not found.");
                continue;
            }

            var original = AccessTools.Method(gameType, target.MethodName);
            if (original == null)
            {
                log.LogWarning($"Patch skipped: {target.TypeName}.{target.MethodName} was not found.");
                continue;
            }

            var bridgeMethod = AccessTools.Method(typeof(RoletopiaGameBridge), target.BridgeMethodName);
            if (bridgeMethod == null)
            {
                log.LogError($"Patch skipped: bridge method {target.BridgeMethodName} was not found.");
                continue;
            }

            try
            {
                harmony.Patch(original, postfix: new HarmonyMethod(bridgeMethod));
                installed++;
                log.LogInfo($"Patched {target.TypeName}.{target.MethodName}.");
            }
            catch (Exception exception)
            {
                log.LogError($"Could not patch {target.TypeName}.{target.MethodName}: {exception}");
            }
        }

        return installed;
    }
}
