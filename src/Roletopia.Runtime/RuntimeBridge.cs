using System;
using System.Collections.Generic;
using System.Linq;
using Roletopia.CoreEngine;
using Roletopia.RoleSystem;

namespace Roletopia.Runtime
{
    public sealed class RoleOption
    {
        public RoleOption(RoleType role, bool enabled, int count)
        {
            Role = role;
            Enabled = enabled;
            Count = Math.Max(0, count);
        }

        public RoleType Role { get; }
        public bool Enabled { get; set; }
        public int Count { get; set; }
    }

    public sealed class HostModSettings
    {
        private readonly Dictionary<RoleType, RoleOption> _roles;

        public HostModSettings()
        {
            _roles = Enum.GetValues(typeof(RoleType))
                .Cast<RoleType>()
                .ToDictionary(role => role, role => new RoleOption(role, true, 1));
        }

        public bool RoletopiaEnabled { get; set; } = true;
        public int RandomSeed { get; set; }
        public IReadOnlyCollection<RoleOption> Roles => _roles.Values.ToArray();

        public RoleOption GetRole(RoleType role) => _roles[role];

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
        void ShowHostMessage(string message);
        void AssignRole(string playerId, RoleType role);
        void ClearRoletopiaHud();
        void SetRoletopiaHudVisible(bool visible);
        void BroadcastSettings(HostModSettings settings);
    }

    public sealed class RuntimeCoordinator
    {
        private readonly GameEngine _engine;
        private readonly RoleManager _roles;
        private readonly IAmongUsRuntimeAdapter _adapter;

        public RuntimeCoordinator(GameEngine engine, RoleManager roles, IAmongUsRuntimeAdapter adapter)
        {
            _engine = engine ?? throw new ArgumentNullException(nameof(engine));
            _roles = roles ?? throw new ArgumentNullException(nameof(roles));
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
        }

        public HostModSettings Settings { get; } = new HostModSettings();

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
                _engine.AddPlayer(playerId);
            }

            _adapter.BroadcastSettings(Settings);
            return true;
        }

        public bool AssignConfiguredRoles()
        {
            if (!_adapter.IsHost || !Settings.RoletopiaEnabled) return false;

            var players = _adapter.ConnectedPlayerIds.OrderBy(id => id, StringComparer.Ordinal).ToArray();
            var pool = Settings.BuildRolePool();
            if (players.Length == 0 || pool.Count == 0) return false;

            var assignments = _roles.AssignRoles(players, pool, Settings.RandomSeed);
            foreach (var assignment in assignments)
            {
                _adapter.AssignRole(assignment.Key, assignment.Value);
            }

            return assignments.Count > 0;
        }
    }
}
