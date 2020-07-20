using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.Collections;
using Unity.Entities;
using Unity.PerformanceTesting;
using UnityEngine;

namespace Unity.Entities.Editor.Tests
{
    class EntityHierarchyQueryBuilderTests
    {
        EntityHierarchyQueryBuilder m_Builder;

        [OneTimeSetUp]
        public void Setup()
        {
            m_Builder = new EntityHierarchyQueryBuilder();
            m_Builder.Initialize();
        }

        public static IEnumerable ParseQueryCaseSource()
        {
            yield return new TestCaseData("       ", false);
            yield return new TestCaseData("", false);
            yield return new TestCaseData(string.Empty, false);
            yield return new TestCaseData("some random string", false);
            yield return new TestCaseData("c:SomeTypeThatDoesntExist", false);
            yield return new TestCaseData($"c:{nameof(EcsTestData)}", true);
            yield return new TestCaseData($"c: {nameof(EcsTestData)}", true);
            yield return new TestCaseData($"C:{nameof(EcsTestData)}", true);
            yield return new TestCaseData($"C: {nameof(EcsTestData)}", true);
            yield return new TestCaseData($"someothertextc:{nameof(EcsTestData)}", false);
            yield return new TestCaseData($"c:!{nameof(EcsTestData)}", false);
            yield return new TestCaseData($"C:!{nameof(EcsTestData)}", false);
            yield return new TestCaseData($"c: !{nameof(EcsTestData)}", false);
            yield return new TestCaseData($"c:{nameof(EcsTestData2)} c:{nameof(EcsTestData)}", true);
            yield return new TestCaseData($"c:{nameof(EcsTestData2)} c:{nameof(EcsTestSharedComp)}", true);
            yield return new TestCaseData($"with c:{nameof(EcsTestData2)} some c:{nameof(EcsTestData)} text c:{nameof(EcsTestSharedComp)} in between", true);
        }

        [TestCaseSource(nameof(ParseQueryCaseSource))]
        public void QueryBuilder_ParseQuery(string input, bool isQueryExpected)
        {
            var r = m_Builder.BuildQuery(input);
            if (isQueryExpected)
                Assert.That(r.QueryDesc, Is.Not.Null, $"input \"{input}\" should be a valid input to build a query");
            else
                Assert.That(r.QueryDesc, Is.Null, $"input \"{input}\" should not be a valid input to build a query");
        }

        [Test]
        [TestCaseSource(nameof(EnumerateAllCategories))]
        public void QueryBuilder_ResultQueryContainsExpectedTypes(TypeManager.TypeCategory typeCategory)
        {
            var componentType = TypeManager.AllTypes.First(x => x.Category == typeCategory && x.Type != null && x.Type.Name != x.Type.FullName).Type;

            var r = m_Builder.BuildQuery($"c:{componentType.FullName}");
            Assert.That(r.IsValid, Is.True);
            Assert.That(r.QueryDesc.Any, Is.EquivalentTo(new ComponentType[] { componentType }));
            Assert.That(r.QueryDesc.None, Is.Empty);
            Assert.That(r.QueryDesc.All, Is.Empty);
        }

        [Test]
        public void QueryBuilder_ExtractUnmatchedString()
        {
            var r = m_Builder.BuildQuery($"with c:{nameof(EcsTestData2)} some c:{nameof(EcsTestData)} text c:{nameof(EcsTestSharedComp)} in between");
            Assert.That(r.Filter, Is.EqualTo("with  some  text  in between"));
        }

        [Test]
        public void QueryBuilder_ReportStatusAndErrorComponentType()
        {
            var erroneousComponentType = nameof(EcsTestData) + Guid.NewGuid().ToString("N");
            var r = m_Builder.BuildQuery($"c:{erroneousComponentType}");

            Assert.That(r, Is.EqualTo(EntityHierarchyQueryBuilder.Result.Invalid(erroneousComponentType)));
        }

        [Test]
        public void QueryBuilder_ExtractUnmatchedStringWhenNotMatchingAnything()
        {
            var r = m_Builder.BuildQuery($"hola");
            Assert.That(r.QueryDesc, Is.Null);
            Assert.That(r.Filter, Is.EqualTo("hola"));
        }

        [Test, Performance]
        public void QueryBuilder_PerformanceTests()
        {
            var types = TypeManager
                .GetAllTypes()
                .Where(t => t.Type != null && (t.Category == TypeManager.TypeCategory.ComponentData || t.Category == TypeManager.TypeCategory.ISharedComponentData)).Take(50).ToArray();
            var inputString = new StringBuilder();
            for (var i = 0; i < types.Length; i++)
            {
                inputString.AppendFormat("c:{0}{1} ble ", i % 2 == 0 ? "!" : string.Empty, types[i].Type.Namespace);
            }

            var input = inputString.ToString();

            Measure.Method(() =>
                {
                    m_Builder.BuildQuery(input);
                })
                .SampleGroup($"Build query from string input containing {types.Length} type constraints")
                .WarmupCount(10)
                .MeasurementCount(1000)
                .Run();
        }

        static IEnumerable EnumerateAllCategories()
        {
            var values = Enum.GetValues(typeof(TypeManager.TypeCategory));
            foreach (var value in values)
            {
                yield return value;
            }
        }

        [Test]
        [TestCaseSource(nameof(EnumerateAllCategories))]
        public void QueryBuilder_EnsureAtLeastOneTypeExistsPerCategory(TypeManager.TypeCategory category)
        {
            Assert.That(TypeManager.AllTypes.Where(x => x.Category == category), Is.Not.Empty);
        }

        [Test]
        [TestCaseSource(nameof(EnumerateAllCategories))]
        public void QueryBuilder_EnsureAllCategoriesAreSupportedInQuery(TypeManager.TypeCategory category)
        {
            var w = new World("test");
            try
            {
                var t = TypeManager.AllTypes.First(x => x.Category == category && x.Type != null);
                using (var q = w.EntityManager.CreateEntityQuery(t.Type))
                {
                    Assert.DoesNotThrow(() =>
                    {
                        var entities = q.ToEntityArray(Allocator.TempJob);
                        entities.Dispose();
                    });
                }
            }
            finally
            {
                w.Dispose();
            }

        }

        [Test]
        public void QueryBuilderTypeCache_IndexTypesAndAllowDuplicateNames()
        {
            var type1 = typeof(Unity.Tests.NamespaceA.TestComponent);
            var type2 = typeof(Unity.Tests.NamespaceB.TestComponent);
            var type3 = typeof(global::TestComponent);

            Assert.That(TypeManager.GetAllTypes().SingleOrDefault(t => t.Type == type1), Is.Not.Null, $"Type {type1} must be present in {nameof(TypeManager)}");
            Assert.That(TypeManager.GetAllTypes().SingleOrDefault(t => t.Type == type2), Is.Not.Null, $"Type {type2} must be present in {nameof(TypeManager)}");
            Assert.That(TypeManager.GetAllTypes().SingleOrDefault(t => t.Type == type3), Is.Not.Null, $"Type {type3} must be present in {nameof(TypeManager)}");

            var typeCache = new EntityHierarchyQueryBuilder.TypeCache();
            typeCache.Initialize();

            var rotationTypes = typeCache.GetMatchingTypes("testcomponent").ToArray();
            Assert.That(rotationTypes, Is.EquivalentTo(new[] { type1, type2, type3 }));

            var type = typeCache.GetMatchingTypes(type1.FullName);
            Assert.That(type, Is.EquivalentTo(new[] { type1 }));

            type = typeCache.GetMatchingTypes(type2.FullName);
            Assert.That(type, Is.EquivalentTo(new[] { type2 }));

            // Equivalent to searching for "testcomponent" directly.
            // Will match all types named "TestComponent" regardless of the namespace
            type = typeCache.GetMatchingTypes(type3.FullName);
            Assert.That(type, Is.EquivalentTo(new[] { type1, type2, type3 }));
        }
    }
}

namespace Unity.Tests.NamespaceA
{
    struct TestComponent : IComponentData { }
}

namespace Unity.Tests.NamespaceB
{
    struct TestComponent : IComponentData { }
}

// Global component type for query builder tests
struct TestComponent : IComponentData
{
}
