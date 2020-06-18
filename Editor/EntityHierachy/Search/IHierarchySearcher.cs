using System;
using System.Collections.Generic;

namespace Unity.Entities.Editor
{
    interface IHierarchySearcher : IDisposable
    {
        bool IsDirty { get; }
        void UpdateRoots(IEnumerable<EntityHierarchyItem> rootItems);
        void Rebuild();
        void Search(List<string> patterns, List<EntityHierarchyItem> results);
    }
}
