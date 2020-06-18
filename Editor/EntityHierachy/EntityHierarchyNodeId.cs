using System;

namespace Unity.Entities.Editor
{
    readonly struct EntityHierarchyNodeId : IEquatable<EntityHierarchyNodeId>, IComparable<EntityHierarchyNodeId>
    {
        public readonly NodeKind Kind;
        public readonly int Id;
        public readonly int Version;

        public EntityHierarchyNodeId(NodeKind kind, int id, int version)
            => (Kind, Id, Version) = (kind, id, version);

        public static EntityHierarchyNodeId FromEntity(Entity entity)
            => new EntityHierarchyNodeId(NodeKind.Entity, entity.Index, entity.Version);

        public static EntityHierarchyNodeId FromScene(int sceneId)
            => new EntityHierarchyNodeId(NodeKind.Scene, sceneId, 0);

        public static EntityHierarchyNodeId FromSubScene(int subsSceneId)
            => new EntityHierarchyNodeId(NodeKind.SubScene, subsSceneId, 0);

        public static readonly EntityHierarchyNodeId Root = new EntityHierarchyNodeId(NodeKind.Root, 0, 0);

        public bool Equals(EntityHierarchyNodeId other)
        {
            return Kind == other.Kind && Id == other.Id && Version == other.Version;
        }

        public override bool Equals(object obj)
        {
            return obj is EntityHierarchyNodeId other && Equals(other);
        }

        public int CompareTo(EntityHierarchyNodeId other)
        {
            var kindComparison = ((byte)Kind).CompareTo((byte)other.Kind);
            if (kindComparison != 0) return kindComparison;
            var idComparison = Id.CompareTo(other.Id);
            if (idComparison != 0) return idComparison;
            return Version.CompareTo(other.Version);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = (int)Kind;
                hashCode = (hashCode * 397) ^ Id;
                hashCode = (hashCode * 397) ^ Version;
                return hashCode;
            }
        }

        public static bool operator ==(EntityHierarchyNodeId left, EntityHierarchyNodeId right)
            => left.Equals(right);

        public static bool operator !=(EntityHierarchyNodeId left, EntityHierarchyNodeId right)
            => !left.Equals(right);

        public override string ToString() => Equals(Root) ? "Root" : $"{Kind}({Id}:{Version})";
    }

    enum NodeKind : byte
    {
        None = 0,
        Root = 1,
        Entity = 2,
        Scene = 3,
        SubScene = 4,
    }
}
