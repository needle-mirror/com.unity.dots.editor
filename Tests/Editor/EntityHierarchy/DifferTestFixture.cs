using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;

namespace Unity.Entities.Editor.Tests
{
    abstract class DifferTestFixture
    {
        World m_World;
        Dictionary<ComponentType, Action<EntityManager, Entity, int>> m_ComponentDataInitializer;

        protected World World => m_World;

        [SetUp]
        public virtual void Setup()
        {
            m_World = new World("TestWorld");
        }

        [TearDown]
        public virtual void Teardown()
        {
            m_World.Dispose();
        }

        protected void CreateEntitiesWithMockSharedComponentData(int count, params ComponentType[] components)
            => CreateEntitiesWithMockSharedComponentData(count, Allocator.TempJob, components).Dispose();

        protected NativeArray<Entity> CreateEntitiesWithMockSharedComponentData(int count, Allocator allocator, params ComponentType[] components)
        {
            var startIndex = m_World.EntityManager.Debug.EntityCount;
            var archetype = m_World.EntityManager.CreateArchetype(components);
            var entities = m_World.EntityManager.CreateEntity(archetype, count, allocator);

            if (components.Any(t => t == typeof(EcsTestSharedComp)))
            {
                for (var i = 0; i < count; i++)
                {
                    World.EntityManager.SetSharedComponentData(entities[i], new EcsTestSharedComp { value = i / 31 });
                }
            }

            return entities;
        }
    }
}