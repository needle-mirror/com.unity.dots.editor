namespace Unity.Entities.Editor
{
    interface IComponentSystemNode
    {
        ComponentSystemBase System { get; }
    }

    abstract class ComponentSystemBaseNode<TSystem, TNode> : PlayerLoopNode<TSystem>, IComponentSystemNode
        where TSystem : ComponentSystemBase
        where TNode : ComponentSystemBaseNode<TSystem, TNode>, new()
    {
        public override string Name => UnityEditor.ObjectNames.NicifyVariableName(Value.GetType().Name);

        public override string FullName => Value.GetType().FullName;
        public override string NameWithWorld => Name + " (" + Value.World?.Name + ")";
        public override bool Enabled
        {
            get => Value.Enabled;
            set => Value.Enabled = value;
        }
        public override bool EnabledInHierarchy => Enabled && (Parent?.EnabledInHierarchy ?? true);

        public ComponentSystemBase System => Value;
        public override int Hash
        {
            get
            {
                unchecked
                {
                    var hashCode = NameWithWorld.GetHashCode();
                    hashCode = (hashCode * 397) ^ (Parent?.Name.GetHashCode() ?? 0);
                    hashCode = (hashCode * 397) ^ Value.GetHashCode();
                    return hashCode;
                }
            }
        }

        public override bool ShowForWorld(World world)
        {
            if (Value.World == null)
                return false;

            if (null == world)
                return true;

            foreach (var child in Children)
            {
                if (child.ShowForWorld(world))
                    return true;
            }

            return Value.World == world;
        }

        public override void Reset()
        {
            base.Reset();
            Value = null;
        }

        public override void ReturnToPool()
        {
            base.ReturnToPool();
            Pool<TNode>.Release((TNode)this);
        }

        public override bool IsRunning => Value.ShouldRunSystem();
    }

    class ComponentGroupNode : ComponentSystemBaseNode<ComponentSystemGroup, ComponentGroupNode>
    {
    }

    class ComponentSystemBaseNode : ComponentSystemBaseNode<ComponentSystemBase, ComponentSystemBaseNode>
    {
    }
}
