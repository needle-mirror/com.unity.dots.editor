using System.Collections.Generic;

namespace Unity.Entities.Editor
{
    static class EntityHierarchyState
    {
        static Dictionary<TreeViewItemStateKey, bool> FoldingState => Unity.Serialization.Editor.SessionState<Dictionary<TreeViewItemStateKey, bool>>.GetOrCreate(typeof(EntityHierarchyState).FullName);

        public static void OnFoldingStateChanged(in EntityHierarchyNodeId nodeId, bool isExpanded)
        {
            if (nodeId.Kind != NodeKind.Scene && nodeId.Kind != NodeKind.SubScene)
                return;

            FoldingState[nodeId] = isExpanded;
        }

        public static bool? GetFoldingState(in EntityHierarchyNodeId nodeId)
            => FoldingState.TryGetValue(nodeId, out var isExpanded) ? isExpanded : (bool?)null;

        internal struct TreeViewItemStateKey
        {
            public NodeKind Kind;
            public int Id;

            public static implicit operator TreeViewItemStateKey(in EntityHierarchyNodeId nodeId)
                => new TreeViewItemStateKey { Kind = nodeId.Kind, Id = nodeId.Id };
        }
    }
}
