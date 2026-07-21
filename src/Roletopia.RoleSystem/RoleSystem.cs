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
        public RoleContext(GameEngine engine, string actorId, string targetId, DateTimeOffset now)
        {
            Engine = engine ?? throw new ArgumentNullException(nameof(engine));
            ActorId = actorId;
            TargetId = targetId;
            Now = now;
        }
        public GameEngine Engine { get; }
        public string ActorId { get; }
        public string TargetId { get; }
        public DateTimeOffset Now { get; }
    }

    public sealed class AbilityResult
    {
        public AbilityResult(AbilityResultCode code, string message)
        {
            Code = code;
            Message = message ?? string.Empty;
        }
        public AbilityResultCode Code { get; }
        public string Message { get; }
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
            if (result.Succeeded) _cooldowns.Start(actor.Id, Definition.RoleType, context.Now, Definition.CooldownSeconds);
            return result;
        }

        protected abstract AbilityResult Execute(RoleContext context);
    }

    public sealed class EliminationRoleBehavior : RoleBehaviorBase
    {
        public EliminationRoleBehavior(RoleDefinition definition, AbilityCooldownTracker cooldowns) : base(definition, cooldowns) { }
        protected override AbilityResult Execute(RoleContext context) =>
            context.Engine.EliminatePlayer(context.TargetId)
                ? new AbilityResult(AbilityResultCode.Success, "Target eliminated.")
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
        public IRoleBehavior Get(RoleType roleType) => _roles.TryGetValue(roleType, out var behavior) ? behavior : null;
        public void Register(IRoleBehavior behavior)
        {
            if (behavior == null) throw new ArgumentNullException(nameof(behavior));
            _roles[behavior.Definition.RoleType] = behavior;
        }
        public void ResetCooldowns() => _cooldowns.Clear();

        private void RegisterBuiltInRoles()
        {
            Register(new EliminationRoleBehavior(new RoleDefinition(RoleType.Sheriff, TeamType.Crewmate, 30, true), _cooldowns));
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

        public IReadOnlyDictionary<string, RoleType> Assign(GameEngine engine, IEnumerable<RoleType> enabledRoles, int seed)
        {
            if (engine == null) throw new ArgumentNullException(nameof(engine));
            var players = engine.Players.Where(p => p.IsConnected).OrderBy(p => p.Id, StringComparer.Ordinal).ToList();
            var roles = (enabledRoles ?? Enumerable.Empty<RoleType>()).ToList();
            if (roles.Count > players.Count) throw new InvalidOperationException("There are more configured role slots than connected players.");

            var random = new Random(seed);
            for (var i = players.Count - 1; i > 0; i--)
            {
                var j = random.Next(i + 1);
                var temp = players[i]; players[i] = players[j]; players[j] = temp;
            }

            var assignments = new Dictionary<string, RoleType>(StringComparer.Ordinal);
            for (var i = 0; i < roles.Count; i++)
            {
                var behavior = _registry.Get(roles[i]) ?? throw new InvalidOperationException("Role is not registered: " + roles[i]);
                engine.AssignRole(players[i].Id, roles[i].ToString(), behavior.Definition.Team);
                assignments[players[i].Id] = roles[i];
            }
            return assignments;
        }
    }
}
