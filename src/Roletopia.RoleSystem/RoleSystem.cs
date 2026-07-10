using System;
using System.Collections.Generic;

namespace Roletopia.RoleSystem
{
    public enum RoleType
    {
        Sheriff,
        Medium,
        Snitch,
        Engineer,
        Guardian,
        Arsonist,
        Jester,
        Hacker,
        Ninja,
        Assassin,
        Dragon
    }

    public sealed class RoleContext
    {
        public RoleContext(string actorId, string targetId)
        {
            ActorId = actorId;
            TargetId = targetId;
        }

        public string ActorId { get; }
        public string TargetId { get; }
    }

    public interface IRoleBehavior
    {
        RoleType RoleType { get; }
        bool CanUseAbility(RoleContext context);
        void UseAbility(RoleContext context);
    }

    public abstract class RoleBehaviorBase : IRoleBehavior
    {
        protected RoleBehaviorBase(RoleType roleType)
        {
            RoleType = roleType;
        }

        public RoleType RoleType { get; }

        public virtual bool CanUseAbility(RoleContext context)
        {
            return context != null && !string.IsNullOrWhiteSpace(context.ActorId);
        }

        public virtual void UseAbility(RoleContext context)
        {
        }
    }

    public sealed class StubRoleBehavior : RoleBehaviorBase
    {
        public StubRoleBehavior(RoleType roleType) : base(roleType)
        {
        }
    }

    public sealed class RoleRegistry
    {
        private readonly Dictionary<RoleType, IRoleBehavior> _roles = new Dictionary<RoleType, IRoleBehavior>();

        public RoleRegistry()
        {
            RegisterBuiltInRoles();
        }

        public IEnumerable<IRoleBehavior> AllRoles => _roles.Values;

        public IRoleBehavior Get(RoleType roleType)
        {
            IRoleBehavior behavior;
            return _roles.TryGetValue(roleType, out behavior) ? behavior : null;
        }

        public void Register(IRoleBehavior behavior)
        {
            if (behavior == null)
            {
                throw new ArgumentNullException(nameof(behavior));
            }

            _roles[behavior.RoleType] = behavior;
        }

        private void RegisterBuiltInRoles()
        {
            Register(new StubRoleBehavior(RoleType.Sheriff));
            Register(new StubRoleBehavior(RoleType.Medium));
            Register(new StubRoleBehavior(RoleType.Snitch));
            Register(new StubRoleBehavior(RoleType.Engineer));
            Register(new StubRoleBehavior(RoleType.Guardian));
            Register(new StubRoleBehavior(RoleType.Arsonist));
            Register(new StubRoleBehavior(RoleType.Jester));
            Register(new StubRoleBehavior(RoleType.Hacker));
            Register(new StubRoleBehavior(RoleType.Ninja));
            Register(new StubRoleBehavior(RoleType.Assassin));
            Register(new StubRoleBehavior(RoleType.Dragon));
        }
    }
}
