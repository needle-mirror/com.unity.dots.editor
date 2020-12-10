using UnityEngine.LowLevel;

namespace Unity.Entities.Editor
{
    interface IPlayerLoopSystemData
    {
        PlayerLoopSystem PlayerLoopSystem { get; }
    }

    class PlayerLoopSystemNode : PlayerLoopNode<PlayerLoopSystem>, IPlayerLoopSystemData
    {
        public override string Name
        {
            get
            {
                var type = Value.type;
                return type == null ? string.Empty : UnityEditor.ObjectNames.NicifyVariableName(Properties.Editor.TypeUtility.GetTypeDisplayName(type));
            }
        }
        public override string NameWithWorld => Name;
        public override string FullName
        {
            get
            {
                var type = Value.type;
                return type == null ? string.Empty : $"{type.Namespace}{(null == type.Namespace ? "" : ".")}{Properties.Editor.TypeUtility.GetTypeDisplayName(type)}";
            }
        }
        public override bool Enabled
        {
            get => true;
            set { }
        }

        public override bool EnabledInHierarchy => Enabled && (Parent?.EnabledInHierarchy ?? true);
        public override int Hash => FullName.GetHashCode();

        public PlayerLoopSystem PlayerLoopSystem => Value;

        public override bool ShowForWorld(World world)
        {
            if (null == world)
                return true;

            foreach (var child in Children)
            {
                if (child.ShowForWorld(world))
                    return true;
            }

            return false;
        }

        public override bool IsRunning => true;

        public override void ReturnToPool()
        {
            base.ReturnToPool();
            Pool<PlayerLoopSystemNode>.Release(this);
        }
    }
}
