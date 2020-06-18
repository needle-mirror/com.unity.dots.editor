using System.Collections;
using System.Linq;
using System.Text;
using NUnit.Framework;
using Unity.PerformanceTesting;

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
            var query = m_Builder.BuildQuery(input, out _);
            if (isQueryExpected)
                Assert.That(query, Is.Not.Null, $"input \"{input}\" should be a valid input to build a query");
            else
                Assert.That(query, Is.Null, $"input \"{input}\" should not be a valid input to build a query");
        }

        [Test]
        public void QueryBuilder_ResultQueryContainsExpectedTypes()
        {
            var query = m_Builder.BuildQuery($"with c:{nameof(EcsTestData2)} some c:{nameof(EcsTestData)} text c:{nameof(EcsTestSharedComp)} in between", out _);
            Assert.That(query.Any, Is.EquivalentTo(new ComponentType[] { typeof(EcsTestData2), typeof(EcsTestData), typeof(EcsTestSharedComp) }));
            Assert.That(query.None, Is.Empty);
            Assert.That(query.All, Is.Empty);
        }

        [Test]
        public void QueryBuilder_ExtractUnmatchedString()
        {
            m_Builder.BuildQuery($"with c:{nameof(EcsTestData2)} some c:{nameof(EcsTestData)} text c:{nameof(EcsTestSharedComp)} in between", out var unmatchedInput);
            Assert.That(unmatchedInput, Is.EqualTo("with  some  text  in between"));
        }

        [Test]
        public void QueryBuilder_ExtractUnmatchedStringWhenNotMatchingAnything()
        {
            var q = m_Builder.BuildQuery($"hola", out var unmatchedInput);
            Assert.That(q, Is.Null);
            Assert.That(unmatchedInput, Is.EqualTo("hola"));
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
                    m_Builder.BuildQuery(input, out _);
                })
                .SampleGroup($"Build query from string input containing {types.Length} type constraints")
                .WarmupCount(10)
                .MeasurementCount(1000)
                .Run();
        }
    }
}
