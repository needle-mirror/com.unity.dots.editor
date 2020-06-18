using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;

namespace Unity.Entities.Editor.Tests
{
    class EntityHierarchyStateTests
    {
        Dictionary<EntityHierarchyState.TreeViewItemStateKey, bool> m_PreviousData;

        static Dictionary<EntityHierarchyState.TreeViewItemStateKey, bool> InternalStateAccessor
            => Unity.Serialization.Editor.SessionState<Dictionary<EntityHierarchyState.TreeViewItemStateKey, bool>>.GetOrCreate(typeof(EntityHierarchyState).FullName);

        [SetUp]
        public void Setup()
        {
            m_PreviousData = InternalStateAccessor.ToDictionary(k => k.Key, v => v.Value);
            InternalStateAccessor.Clear();
        }

        [TearDown]
        public void Teardown()
        {
            InternalStateAccessor.Clear();
            foreach (var kvp in m_PreviousData)
            {
                InternalStateAccessor[kvp.Key] = kvp.Value;
            }
        }

        [Test]
        public void ShouldPersistState()
        {
            var subSceneA = EntityHierarchyNodeId.FromSubScene(1);
            var subSceneB = EntityHierarchyNodeId.FromSubScene(2);
            var unknownSubScene = EntityHierarchyNodeId.FromSubScene(3);

            EntityHierarchyState.OnFoldingStateChanged(subSceneA, true);
            EntityHierarchyState.OnFoldingStateChanged(subSceneB, false);

            Assert.That(EntityHierarchyState.GetFoldingState(subSceneA), Is.True);
            Assert.That(EntityHierarchyState.GetFoldingState(subSceneB), Is.False);
            Assert.That(EntityHierarchyState.GetFoldingState(unknownSubScene), Is.Null);
        }

        [Test]
        public void ShouldIgnoreEverythingExceptSceneAndSubScenes()
        {
            EntityHierarchyState.OnFoldingStateChanged(EntityHierarchyNodeId.Root, true);
            EntityHierarchyState.OnFoldingStateChanged(EntityHierarchyNodeId.FromEntity(new Entity { Index = 1, Version = 1 }), true);
            EntityHierarchyState.OnFoldingStateChanged(EntityHierarchyNodeId.FromScene(1), true);
            EntityHierarchyState.OnFoldingStateChanged(EntityHierarchyNodeId.FromSubScene(1), false);

            Assert.That(EntityHierarchyState.GetFoldingState(EntityHierarchyNodeId.Root), Is.Null);
            Assert.That(EntityHierarchyState.GetFoldingState(EntityHierarchyNodeId.FromEntity(new Entity { Index = 1, Version = 1 })), Is.Null);
            Assert.That(EntityHierarchyState.GetFoldingState(EntityHierarchyNodeId.FromScene(1)), Is.True);
            Assert.That(EntityHierarchyState.GetFoldingState(EntityHierarchyNodeId.FromSubScene(1)), Is.False);
        }
    }
}
