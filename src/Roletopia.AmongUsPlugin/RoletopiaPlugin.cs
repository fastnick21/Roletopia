using BepInEx;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Roletopia.CoreEngine;
using Roletopia.RoleSystem;
using Roletopia.Runtime;

namespace Roletopia.AmongUsPlugin;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
public sealed class RoletopiaPlugin : BasePlugin
{
    public const string PluginGuid = "com.roletopia.mod";
    public const string PluginName = "Roletopia";
    public const string PluginVersion = "0.1.0-alpha";

    private Harmony? _harmony;

    public override void Load()
    {
        try
        {
            Log.LogInfo($"Loading {PluginName} {PluginVersion}...");

            var engine = new GameEngine();
            var registry = new RoleRegistry();
            var adapter = new ReflectionAmongUsAdapter(Log);
            var coordinator = new RuntimeCoordinator(engine, new RoleAssignmentService(registry), adapter);
            var lifecycle = new AmongUsLifecycleController(engine, coordinator, new BepInExRuntimeLogger(Log));

            engine.GameWon += coordinator.ApplyWinResult;

            RoletopiaGameBridge.Initialize(lifecycle, coordinator, adapter, Log);
            MainMenuMarkerPatch.Initialize(Log);
            HostRoleSidebarPatch.Initialize(Log);

            _harmony = new Harmony(PluginGuid);
            var installed = DynamicPatchInstaller.Install(_harmony, Log);
            _harmony.PatchAll(typeof(MainMenuMarkerPatch).Assembly);
            lifecycle.OnPluginLoaded();

            Log.LogInfo($"Roletopia loaded. Installed {installed} Among Us lifecycle hooks plus visual patches and host role sidebar.");
        }
        catch (Exception exception)
        {
            Log.LogError($"Roletopia failed to load: {exception}");
            throw;
        }
    }

    public override bool Unload()
    {
        _harmony?.UnpatchSelf();
        RoletopiaGameBridge.Reset();
        Log.LogInfo("Roletopia unloaded.");
        return true;
    }
}

internal sealed class BepInExRuntimeLogger : IRuntimeLogger
{
    private readonly ManualLogSource _log;

    public BepInExRuntimeLogger(ManualLogSource log) => _log = log;

    public void Info(string message) => _log.LogInfo(message);
    public void Warning(string message) => _log.LogWarning(message);
    public void Error(string message, Exception exception) => _log.LogError($"{message}: {exception}");
}
