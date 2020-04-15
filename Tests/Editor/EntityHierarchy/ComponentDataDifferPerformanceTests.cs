using NUnit.Framework;
using Unity.Collections;
using Unity.PerformanceTesting;

namespace Unity.Entities.Editor.Tests
{
    [Ignore("Re-enable these tests when performance package is updated")]
    [TestFixture]
    [Category("Performance")]
    class ComponentDataDifferPerformanceTests : DifferTestFixture
    {
        [Test, Performance]
        public unsafe void ComponentDataDiffer_Change_PerformanceTest([Values(100_000, 500_000, 1_000_000)]
                                                                      int entityCount,
                                                                      [Values(1000, 5000, 10_000)]
                                                                      int changeCount)
        {
            var entities = CreateEntitiesWithMockSharedComponentData(entityCount, Allocator.TempJob, typeof(EcsTestData));
            var query = World.EntityManager.CreateEntityQuery(typeof(EcsTestData));
            var componentDiffer = new ComponentDataDiffer(typeof(EcsTestData));
            var counter = entities.Length;
            if (changeCount > entityCount)
                changeCount = entityCount;

            Measure.Method(() =>
                {
                    var result = componentDiffer.GatherComponentChangesAsync(query, Allocator.TempJob, out var handle);
                    handle.Complete();
                    result.Dispose();
                })
                .SetUp(() =>
                {
                    World.EntityManager.EntityComponentStore->IncrementGlobalSystemVersion();
                    for (var i = 0; i < changeCount; i++)
                    {
                        World.EntityManager.SetComponentData(entities[i], new EcsTestData { value = counter++ });
                    }
                })
                //.SampleGroup($"{changeCount} changes over {entityCount} entities")
                .WarmupCount(10)
                .MeasurementCount(100)
                .Run();

            entities.Dispose();
            query.Dispose();
            componentDiffer.Dispose();
        }

        [Test, Performance]
        public void ComponentDataDiffer_Spawn_PerformanceTest([Values(100_000, 250_000, 500_000, 750_000, 1_000_000)]
                                                              int entityCount)
        {
            var entities = CreateEntitiesWithMockSharedComponentData(entityCount, Allocator.TempJob, typeof(EcsTestData));
            var query = World.EntityManager.CreateEntityQuery(typeof(EcsTestData));
            ComponentDataDiffer componentDiffer = null;

            Measure.Method(() =>
                {
                    var result = componentDiffer.GatherComponentChangesAsync(query, Allocator.TempJob, out var handle);
                    handle.Complete();
                    result.Dispose();
                })
                .SetUp(() =>
                {
                    componentDiffer = new ComponentDataDiffer(typeof(EcsTestData));
                })
                .CleanUp(() =>
                {
                    componentDiffer.Dispose();
                })
                //.SampleGroup($"First check over {entityCount} entities")
                .WarmupCount(10)
                .MeasurementCount(100)
                .Run();

            entities.Dispose();
            query.Dispose();
        }
    }
}