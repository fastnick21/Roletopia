using System;
using System.Collections.Generic;
using System.Linq;
using Roletopia.CoreEngine;

namespace Roletopia.RoleSystem
{
    public enum RoleType
    {
        Sheriff, Medium, Snitch, Engineer, Guardian,
        Arsonist, Jester, Hacker, Ninja, Assassin, Dragon
    }

    public enum AbilityResultCode { Success, InvalidActor, InvalidTarget, OnCooldown, WrongPhase, NotSupported }

    public sealed class RoleDefinition
    {
        public RoleDefinition(RoleType roleType, TeamType team, double cooldownSeconds, bool requiresTarget)
        {
            RoleType = roleType;
            Team = team;
            CooldownSeconds = Math.Max(0, cooldownSeconds);
            RequiresTarget = requiresTarget;
        }
        public RoleType RoleType { get; }
        public TeamType Team { get; }
        public double CooldownSeconds { get; }
        public bool RequiresTarget { get; }
    }

    public sealed class RoleContext
    {
        public RoleContext(
            GameEngine engine,
            string actorId,
            string targetId,
            DateTimeOffset now,
            double? cooldownSecondsOverride = null,
            bool sheriffMisfireKillsSelf = true)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            ActorId = actorId;
            TargetId = targetId;
            Now = now;
            CooldownSecondsOverride = cooldownSecondsOverride;
            SheriffMisfireKillsSelf = sheriffMisfireKillsSelf;
        }
        public GameEngine Engine { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public DateTimeOffset Now { get; }
        public double? CooldownSecondsOverride { get; }
        public bool SheriffMisfireKillsSelf { get; }
    }

    public sealed class AbilityResult
    {
        public AbilityResult(AbilityResultCode code, string message, string? eliminatedPlayerId = null)
        {
            Code = code;
            Message = message ?? string.Empty;
            EliminatedPlayerId = eliminatedPlayerId;
        }
        public AbilityResultCode Code { get; }
        public string Message { get; }
        public string? EliminatedPlayerId { get; }
        public bool Succeeded => Code == AbilityResultCode.Success;
    }

    public interface IRoleBehavior
    {
        RoleDefinition Definition { get; }
        AbilityResult UseAbility(RoleContext context);
    }

    public sealed class AbilityCooldownTracker
    {
        private readonly Dictionary<string, DateTimeOffset> _readyAt = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        private static string Key(string playerId, RoleType role) => playerId + ":" + role;

        public bool IsReady(string playerId, RoleType role, DateTimeOffset now) =>
            !_readyAt.TryGetValue(Key(playerId, role), out var readyAt) || now >= readyAt;

        public TimeSpan Remaining(string playerId, RoleType role, DateTimeOffset now)
        {
            if (!_readyAt.TryGetValue(Key(playerId, role), out var readyAt) || now >= readyAt) return TimeSpan.Zero;
            return readyAt - now;
        }

        public void Start(string playerId, RoleType role, DateTimeOffset now, double seconds) =>
            _readyAt[Key(playerId, role)] = now.AddSeconds(Math.Max(0, seconds));

        public void Clear() => _readyAt.Clear();
    }

    public abstract class RoleBehaviorBase : IRoleBehavior
    {
        private readonly AbilityCooldownTracker _cooldowns;
        protected RoleBehaviorBase(RoleDefinition definition, AbilityCooldownTracker cooldowns)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _cooldowns = cooldowns ?? throw new ArgumentNullException(nameof(cooldowns));
        }

        public RoleDefinition Definition { get; }

        public AbilityResult UseAbility(RoleContext context)
        {
            if (context == null || !context.Engine.TryGetPlayer(context.ActorId, out var actor) || !actor.IsAlive || !actor.IsConnected)
                return new AbilityResult(AbilityResultCode.InvalidActor, "The actor is unavailable.");
            if (context.Engine.State.Phase != GamePhase.InProgress)
                return new AbilityResult(AbilityResultCode.WrongPhase, "Abilities can only be used during active gameplay.");
            if (actor.RoleId != Definition.RoleType.ToString())
                return new AbilityResult(AbilityResultCode.InvalidActor, "The actor does not own this role.");
            if (Definition.RequiresTarget && (!context.Engine.TryGetPlayer(context.TargetId, out var target) || !target.IsAlive || !target.IsConnected || target.Id == actor.Id))
                return new AbilityResult(AbilityResultCode.InvalidTarget, "A different living target is required.");
            if (!_cooldowns.IsReady(actor.Id, Definition.RoleType, context.Now))
                return new AbilityResult(AbilityResultCode.OnCooldown, "The ability is still cooling down.");

            var result = Execute(context);
            if (result.Succeeded)
            {
                var cooldown = context.CooldownSecondsOverride ?? Definition.CooldownSeconds;
                _cooldowns.Start(actor.Id, Definition.RoleType, context.Now, cooldown);
            }
            return result;
        }

        protected abstract AbilityResult Execute(RoleContext context);
    }

    public sealed class SheriffRoleBehavior : RoleBehaviorBase
    {
        public SheriffRoleBehavior(AbilityCooldownTracker cooldowns)
            : base(new RoleDefinition(RoleType.Sheriff, TeamType.Crewmate, 30, true), cooldowns) { }

        protected override AbilityResult Execute(RoleContext context)
        {
            if (!context.Engine.TryGetPlayer(context.ActorId, out var sheriff) ||
                !context.Engine.TryGetPlayer(context.TargetId, out var target))
            {
                return new AbilityResult(AbilityResultCode.InvalidTarget, "Sheriff target could not be resolved.");
            }

            if (target.Team == TeamType.Impostor)
            {
                return context.Engine.EliminatePlayer(target.Id)
                    ? new AbilityResult(AbilityResultCode.Success, "Sheriff eliminated an impostor.", target.Id)
                    : new AbilityResult(AbilityResultCode.InvalidTarget, "The impostor could not be eliminated.");
            }

            if (context.SheriffMisfireKillsSelf)
            {
                return context.Engine.EliminatePlayer(sheriff.Id)
                    ? new AbilityResult(AbilityResultCode.Success, "Sheriff misfired and was eliminated.", sheriff.Id)
                    : new AbilityResult(AbilityResultCode.InvalidActor, "Sheriff misfire could not be applied.");
            }

            return context.Engine.EliminatePlayer(target.Id)
                ? new AbilityResult(AbilityResultCode.Success, "Sheriff shot the selected target.", target.Id)
                : new AbilityResult(AbilityResultCode.InvalidTarget, "The selected target could not be eliminated.");
        }
    }

    public sealed class EliminationRoleBehavior : RoleBehaviorBase
    {
        public EliminationRoleBehavior(RoleDefinition definition, AbilityCooldownTracker cooldowns) : base(definition, cooldowns) { }
        protected override AbilityResult Execute(RoleContext context) =>
            context.Engine.EliminatePlayer(context.TargetId)
                ? new AbilityResult(AbilityResultCode.Success, "Target eliminated.", context.TargetId)
                : new AbilityResult(AbilityResultCode.InvalidTarget, "Target could not be eliminated.");
    }

    public sealed class UtilityRoleBehavior : RoleBehaviorBase
    {
        public UtilityRoleBehavior(RoleDefinition definition, AbilityCooldownTracker cooldowns) : base(definition, cooldowns) { }
        protected override AbilityResult Execute(RoleContext context) =>
            new AbilityResult(AbilityResultCode.Success, "Ability accepted by the core engine; the runtime adapter must apply its game effect.");
    }

    public sealed class RoleRegistry
    {
        private readonly Dictionary<RoleType, IRoleBehavior> _roles = new Dictionary<RoleType, IRoleBehavior>();
        private readonly AbilityCooldownTracker _cooldowns = new AbilityCooldownTracker();

        public RoleRegistry() { RegisterBuiltInRoles(); }
        public IEnumerable<IRoleBehavior> AllRoles => _roles.Values;
        public IRoleBehavior? Get(RoleType roleType) => _roles.TryGetValue(roleType, out var behavior) ? behavior : null;
        public void Register(IRoleBehavior behavior)
        {
            if (behavior == null) throw new ArgumentNullException(nameof(behavior));
            _roles[behavior.Definition.RoleType] = behavior;
        }
        public void ResetCooldowns() => _cooldowns.Clear();

        private void RegisterBuiltInRoles()
        {
            Register(new SheriffRoleBehavior(_cooldowns));
            Register(new UtilityRoleBehavior(new RoleDefinition(RoleType.Medium, TeamType.Crewmate, 20, false), _cooldowns));
            Register(new UtilityRoleBehavior(new RoleDefinition(RoleType.Snitch, TeamType.Crewmate, 0, false), _cooldowns));
            Register(new UtilityRoleBehavior(new RoleDefinition(RoleType.Engineer, TeamType.Crewmate, 25, false), _cooldowns));
            Register(new UtilityRoleBehavior(new RoleDefinition(RoleType.Guardian, TeamType.Crewmate, 30, true), _cooldowns));
            Register(new UtilityRoleBehavior(new RoleDefinition(RoleType.Arsonist, TeamType.Neutral, 15, true), _cooldowns));
            Register(new UtilityRoleBehavior(new RoleDefinition(RoleType.Jester, TeamType.Neutral, 0, false), _cooldowns));
            Register(new UtilityRoleBehavior(new RoleDefinition(RoleType.Hacker, TeamType.Impostor, 25, false), _cooldowns));
            Register(new EliminationRoleBehavior(new RoleDefinition(RoleType.Ninja, TeamType.Impostor, 30, true), _cooldowns));
            Register(new EliminationRoleBehavior(new RoleDefinition(RoleType.Assassin, TeamType.Impostor, 35, true), _cooldowns));
            Register(new EliminationRoleBehavior(new RoleDefinition(RoleType.Dragon, TeamType.Neutral, 40, true), _cooldowns));
        }
    }

    public sealed class RoleAssignmentService
    {
        private readonly RoleRegistry _registry;
        public RoleAssignmentService(RoleRegistry registry) { _registry = registry ?? throw new ArgumentNullException(nameof(registry)); }
        public RoleRegistry Registry => _registry;

        public IReadOnlyDictionary<string, RoleType> Assign(GameEngine engine, IEnumerable<RoleType> enabledRoles, int seed)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            var players = engine.Players.Where(p => p.IsConnected).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            var roles = (enabledRoles ?? Enumerable.Empty<RoleType>()).ToList();
            if (roles.Count > players.Count) throw new InvalidOperationException("There are more configured role slots than connected players.");

            var random = new Random(seed);
            var unassigned = players.OrderBy(_ => random.Next()).ToList();
            var assignments = new Dictionary<string, RoleType>(StringComparer.Ordinal);

            foreach (var role in roles)
            {
                var behavior = _registry.Get(role) ?? throw new InvalidOperationException("Role is not registered: " + role);
                var candidatePool = unassigned.Where(player => IsEligibleBaseTeam(player.Team, behavior.Definition.Team)).ToList();
                if (candidatePool.Count == 0)
                    throw new InvalidOperationException($"No eligible player is available for {role} ({behavior.Definition.Team}).");

                var selected = candidatePool[random.Next(candidatePool.Count)];
                engine.AssignRole(selected.Id, role.ToString(), behavior.Definition.Team);
                assignments[selected.Id] = role;
                unassigned.Remove(selected);
            }

            return assignments;
        }

        private static bool IsEligibleBaseTeam(TeamType playerTeam, TeamType roleTeam)
        {
            if (roleTeam == TeamType.Impostor) return playerTeam == TeamType.Impostor;
            return playerTeam != TeamType.Impostor;
        }
    }
}