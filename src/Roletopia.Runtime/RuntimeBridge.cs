using System;
using System.Collections.Generic;
using System.Linq;
using Roletopia.CoreEngine;
using Roletopia.RoleSystem;

namespace Roletopia.Runtime
{
    public sealed class RoleSetting
    {
        public RoleSetting(string key, string displayName, double value, double min, double max, double step)
        {
            Key = key;
            DisplayName = displayName;
            Value = value;
            Min = min;
            Max = max;
            Step = Math.Max(0.01, step);
        }

        public string Key { get; }
        public string DisplayName { get; }
        public double Value { get; set; }
        public double Min { get; }
        public double Max { get; }
        public double Step { get; }

        public void Adjust(int direction) => Value = Math.Clamp(Value + (Step * direction), Min, Max);
    }

    public sealed class RoleOption
    {
        private readonly List<RoleSetting> _settings = new();

        public RoleOption(RoleType role, bool enabled, int count)
        {
            Role = role;
            Enabled = enabled;
            Count = Math.Max(0, count);
        }

        public RoleType Role { get; }
        public bool Enabled { get; set; }
        public int Count { get; set; }
        public IReadOnlyList<RoleSetting> Settings => _settings;

        public RoleOption Add(string key, string displayName, double value, double min, double max, double step)
        {
            _settings.Add(new RoleSetting(key, displayName, value, min, max, step));
            return this;
        }

        public RoleSetting? GetSetting(string key) => _settings.FirstOrDefault(s => s.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
    }

    public sealed class HostModSettings
    {
        private readonly Dictionary<RoleType, RoleOption> _roles;

        public HostModSettings()
        {
            _roles = Enum.GetValues(typeof(RoleType))
                .Cast<RoleType>()
                .ToDictionary(role => role, role => new RoleOption(role, role == RoleType.Sheriff, role == RoleType.Sheriff ? 1 : 0));

            Configure(RoleType.Sheriff, ("cooldown", "Kill Cooldown", 30d, 5d, 90d, 5d), ("misfire", "Misfire Kills Sheriff", 1d, 0d, 1d, 1d));
            Configure(RoleType.Medium, ("cooldown", "Seance Cooldown", 20d, 5d, 90d, 5d), ("duration", "Seance Duration", 8d, 2d, 30d, 1d));
            Configure(RoleType.Snitch, ("tasks", "Tasks Until Reveal", 1d, 0d, 10d, 1d), ("reveal", "Reveal Impostors", 1d, 0d, 1d, 1d));
            Configure(RoleType.Engineer, ("cooldown", "Vent Cooldown", 25d, 0d, 90d, 5d), ("duration", "Max Vent Time", 10d, 1d, 60d, 1d));
            Configure(RoleType.Guardian, ("cooldown", "Shield Cooldown", 30d, 5d, 90d, 5d), ("duration", "Shield Duration", 15d, 2d, 60d, 1d));
            Configure(RoleType.Arsonist, ("cooldown", "Douse Cooldown", 15d, 3d, 60d, 3d), ("douse", "Douse Duration", 3d, 1d, 10d, 1d));
            Configure(RoleType.Jester, ("vent", "Can Vent", 0d, 0d, 1d, 1d), ("tasks", "Has Fake Tasks", 1d, 0d, 1d, 1d));
            Configure(RoleType.Hacker, ("cooldown", "Hack Cooldown", 25d, 5d, 90d, 5d), ("duration", "Hack Duration", 10d, 2d, 30d, 1d));
            Configure(RoleType.Ninja, ("cooldown", "Strike Cooldown", 30d, 5d, 90d, 5d), ("duration", "Invisible Duration", 8d, 1d, 30d, 1d));
            Configure(RoleType.Assassin, ("cooldown", "Guess Cooldown", 35d, 5d, 90d, 5d), ("uses", "Guesses Per Game", 2d, 1d, 10d, 1d));
            Configure(RoleType.Dragon, ("cooldown", "Burn Cooldown", 40d, 5d, 120d, 5d), ("duration", "Burn Duration", 6d, 1d, 30d, 1d));
        }

        public bool RoletopiaEnabled { get; set; } = true;
        public int RandomSeed { get; set; }
        public IReadOnlyCollection<RoleOption> Roles => _roles.Values.OrderBy(r => r.Role).ToArray();

        public RoleOption GetRole(RoleType role) => _roles[role];

        private void Configure(RoleType role, params (string key, string label, double value, double min, double max, double step)[] settings)
        {
            foreach (var setting in settings)
                _roles[role].Add(setting.key, setting.label, setting.value, setting.min, setting.max, setting.step);
        }

        public void DisableAllRoles()
        {
            foreach (var option in _roles.Values)
            {
                option.Enabled = false;
                option.Count = 0;
            }
        }

        public IReadOnlyList<RoleType> BuildRolePool()
        {
            if (!RoletopiaEnabled) return Array.Empty<RoleType>();

            var pool = new List<RoleType>();
            foreach (var option in _roles.Values.OrderBy(option => option.Role))
            {
                if (!option.Enabled || option.Count <= 0) continue;
                for (var i = 0; i < option.Count; i++) pool.Add(option.Role);
            }
            return pool;
        }
    }

    public interface IAmongUsRuntimeAdapter
    {
        bool IsHost { get; }
        IReadOnlyCollection<string> ConnectedPlayerIds { get; }
        TeamType GetPlayerTeam(string playerId);
        void ShowHostMessage(string message);
        void AssignRole(string playerId, RoleType role);
        void ClearRoletopiaHud();
        void SetRoletopiaHudVisible(bool visible);
        void BroadcastSettings(HostModSettings settings);
    }

    public sealed class RuntimeCoordinator
    {
        private readonly GameEngine _engine;
        private readonly RoleAssignmentService _assignments;
        private readonly IAmongUsRuntimeAdapter _adapter;

        public RuntimeCoordinator(GameEngine engine, RoleAssignmentService assignments, IAmongUsRuntimeAdapter adapter)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _assignments = assignments ?? throw new ArgumentNullException(nameof(assignments));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public HostModSettings Settings { get; } = new HostModSettings();
        public bool IsHost => _adapter.IsHost;

        public AbilityResult UseRoleAbility(string actorId, string targetId, DateTimeOffset now)
        {
            if (!_engine.TryGetPlayer(actorId, out var actor) || !Enum.TryParse<RoleType>(actor.RoleId, out var role))
                return new AbilityResult(AbilityResultCode.InvalidActor, "The player does not have a Roletopia role.");

            var behavior = _assignments.Registry.Get(role);
            if (behavior == null)
                return new AbilityResult(AbilityResultCode.NotSupported, "The role behavior is not registered.");

            var option = Settings.GetRole(role);
            var cooldown = option.GetSetting("cooldown")?.Value;
            var misfireKillsSheriff = option.GetSetting("misfire")?.Value >= 0.5;
            return behavior.UseAbility(new RoleContext(
                _engine,
                actorId,
                targetId,
                now,
                cooldown,
                role != RoleType.Sheriff || misfireKillsSheriff));
        }

        public bool ToggleRole(RoleType role)
        {
            if (!_adapter.IsHost) return false;
            var option = Settings.GetRole(role);
            option.Enabled = !option.Enabled;
            if (option.Enabled && option.Count == 0) option.Count = 1;
            _adapter.BroadcastSettings(Settings);
            return true;
        }

        public bool AdjustRoleCount(RoleType role, int delta)
        {
            if (!_adapter.IsHost) return false;
            var option = Settings.GetRole(role);
            option.Count = Math.Clamp(option.Count + delta, 0, 15);
            option.Enabled = option.Count > 0;
            _adapter.BroadcastSettings(Settings);
            return true;
        }

        public bool AdjustRoleSetting(RoleType role, int settingIndex, int direction)
        {
            if (!_adapter.IsHost) return false;
            var option = Settings.GetRole(role);
            if (settingIndex < 0 || settingIndex >= option.Settings.Count) return false;
            option.Settings[settingIndex].Adjust(direction);
            _adapter.BroadcastSettings(Settings);
            return true;
        }

        public string BuildHostSidebarText(RoleType selectedRole, int selectedSetting)
        {
            var lines = new List<string>
            {
                "ROLETOPIA HOST",
                "ROLE SETTINGS",
                ""
            };

            foreach (var option in Settings.Roles)
            {
                var cursor = option.Role == selectedRole ? ">" : " ";
                lines.Add($"{cursor} {option.Role,-10} x{option.Count} {(option.Enabled ? "ON" : "OFF")}");
            }

            var current = Settings.GetRole(selectedRole);
            lines.Add("");
            lines.Add($"{selectedRole} SETTINGS");
            for (var i = 0; i < current.Settings.Count; i++)
            {
                var setting = current.Settings[i];
                var cursor = i == selectedSetting ? ">" : " ";
                var shown = setting.Max == 1 && setting.Min == 0 ? (setting.Value >= 0.5 ? "ON" : "OFF") : setting.Value.ToString("0.##");
                lines.Add($"{cursor} {setting.DisplayName}: {shown}");
            }

            lines.Add("");
            lines.Add("CONTROLS");
            lines.Add("[UP] [DOWN]   Select role");
            lines.Add("[LEFT] [RIGHT] Change quantity");
            lines.Add("[ENTER]       Enable / disable role");
            lines.Add("[ [ ] [ ] ]   Select role setting");
            lines.Add("[-] [+]       Change setting value");
            lines.Add("[F6]          Hide / show sidebar");
            return string.Join("\n", lines);
        }

        public bool ApplyHostToggle(bool enabled)
        {
            if (!_adapter.IsHost) return false;

            Settings.RoletopiaEnabled = enabled;
            _adapter.SetRoletopiaHudVisible(enabled);
            if (!enabled)
            {
                _adapter.ClearRoletopiaHud();
                _adapter.ShowHostMessage("Roletopia is disabled for this lobby. The next game will use normal Among Us rules.");
            }
            else
            {
                _adapter.ShowHostMessage("Roletopia is enabled for this lobby.");
            }

            _adapter.BroadcastSettings(Settings);
            return true;
        }

        public bool PrepareLobby()
        {
            if (!_adapter.IsHost) return false;

            foreach (var playerId in _adapter.ConnectedPlayerIds)
            {
                if (!_engine.AddPlayer(playerId)) continue;
                var baseTeam = _adapter.GetPlayerTeam(playerId);
                _engine.AssignRole(playerId, baseTeam == TeamType.Impostor ? "Impostor" : "Crewmate", baseTeam);
            }

            _adapter.SetRoletopiaHudVisible(Settings.RoletopiaEnabled);
            _adapter.ShowHostMessage(Settings.RoletopiaEnabled
                ? "Roletopia is active. Host role settings are available in the lobby sidebar."
                : "Roletopia is disabled for this lobby.");
            _adapter.BroadcastSettings(Settings);
            return true;
        }

        public bool AssignConfiguredRoles()
        {
            if (!_adapter.IsHost || !Settings.RoletopiaEnabled) return false;

            var players = _adapter.ConnectedPlayerIds.ToArray();
            var configuredPool = Settings.BuildRolePool().ToList();
            if (players.Length == 0 || configuredPool.Count == 0) return false;

            var random = new Random(Settings.RandomSeed);
            for (var i = configuredPool.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                (configuredPool[i], configuredPool[j]) = (configuredPool[j], configuredPool[i]);
            }

            var rolePool = configuredPool.Take(players.Length).ToArray();
            var assignments = _assignments.Assign(_engine, rolePool, Settings.RandomSeed);
            foreach (var assignment in assignments)
                _adapter.AssignRole(assignment.Key, assignment.Value);

            if (assignments.Count > 0)
                _adapter.ShowHostMessage($"Roletopia assigned {assignments.Count} role(s).");

            return assignments.Count > 0;
        }
    }
}