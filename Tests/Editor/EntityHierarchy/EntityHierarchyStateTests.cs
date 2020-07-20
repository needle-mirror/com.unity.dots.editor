using System;
using NUnit.Framework;

namespace Unity.Entities.Editor.Tests
{
    class EntityHierarchyStateTests
    {
        [Test]
        public void ShouldPersistState()
        {
            var state = new EntityHierarchyState(Guid.NewGuid().ToString("N"));

            var subSceneA = EntityHierarchyNodeId.FromSubScene(1);
            var subSceneB = EntityHierarchyNodeId.FromSubScene(2);
            var unknownSubScene = EntityHierarchyNodeId.FromSubScene(3);

            state.OnFoldingStateChanged(subSceneA, true);
            state.OnFoldingStateChanged(subSceneB, false);

            Assert.That(state.GetFoldingState(subSceneA), Is.True);
            Assert.That(state.GetFoldingState(subSceneB), Is.False);
            Assert.That(state.GetFoldingState(unknownSubScene), Is.Null);
        }

        [Test]
        public void ShouldIgnoreEverythingExceptSceneAndSubScenes()
        {
            var state = new EntityHierarchyState(Guid.NewGuid().ToString("N"));

            state.OnFoldingStateChanged(EntityHierarchyNodeId.Root, true);
            state.OnFoldingStateChanged(EntityHierarchyNodeId.FromEntity(new Entity { Index = 1, Version = 1 }), true);
            state.OnFoldingStateChanged(EntityHierarchyNodeId.FromScene(1), true);
            state.OnFoldingStateChanged(EntityHierarchyNodeId.FromSubScene(1), false);

            Assert.That(state.GetFoldingState(EntityHierarchyNodeId.Root), Is.Null);
            Assert.That(state.GetFoldingState(EntityHierarchyNodeId.FromEntity(new Entity { Index = 1, Version = 1 })), Is.Null);
            Assert.That(state.GetFoldingState(EntityHierarchyNodeId.FromScene(1)), Is.True);
            Assert.That(state.GetFoldingState(EntityHierarchyNodeId.FromSubScene(1)), Is.False);
        }
    }
}
