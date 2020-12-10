namespace Unity.Entities.Editor
{
    interface ISystemHandleNode
    {
        SystemHandle SystemHandle { get; }
    }

    abstract class SystemHandleNode<TNode> : PlayerLoopNode<SystemHandle>, ISystemHandleNode
        where TNode : SystemHandleNode<TNode>, new()
    {
        public override string Name
        {
            get
            {
                var type = Value.GetSystemType();
                return type == null ? string.Empty : UnityEditor.ObjectNames.NicifyVariableName(Properties.Editor.TypeUtility.GetTypeDisplayName(type).Replace(".", " | "));
            }
        }
        public override string FullName
        {
            get
            {
                var type = Value.GetSystemType();
                return type == null ? string.Empty : $"{Value.GetSystemType().Namespace}{(null == Value.GetSystemType().Namespace ? "" : ".")}{Properties.Editor.TypeUtility.GetTypeDisplayName(Value.GetSystemType())}";
            }
        }
        public override string NameWithWorld => Name + " (" + Value.World?.Name + ")";

        public override unsafe bool Enabled
        {
            get => Value.StatePointer->Enabled;
            set => Value.StatePointer->Enabled = value;
        }

        public override bool EnabledInHierarchy => Enabled && (Parent?.EnabledInHierarchy ?? true);

        public SystemHandle SystemHandle
        {
            get
            {
                if (Value != null && Value is SystemHandle systemHandle)
                    return systemHandle;

                return null;
            }
        }

        public override int Hash
        {
            get
            {
                unchecked
                {
                    var hashCode = FullName.GetHashCode();
                    hashCode = (hashCode * 397) ^ (Parent?.Name.GetHashCode() ?? 0);
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

        public override unsafe bool IsRunning => Value.StatePointer->ShouldRunSystem();
    }

    class ComponentGroupNode : SystemHandleNode<ComponentGroupNode>
    {
    }

    class SystemHandleNode : SystemHandleNode<SystemHandleNode>
    {
    }
}
