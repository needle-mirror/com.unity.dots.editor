using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.PerformanceTesting;

namespace Unity.Entities.Editor.Tests
{
    class TrieTests
    {
        [Test]
        public void Trie_IndexAndSearch()
        {
            var trie = new Trie();
            trie.Index("bonjour");
            trie.Index("bon");
            trie.Index("bonsoir");

            Assert.That(trie.Search("bon").ToArray(), Is.EquivalentTo(new[] { "bonjour", "bon", "bonsoir" }));
            Assert.That(trie.Search("bonjour").ToArray(), Is.EquivalentTo(new[] { "bonjour" }));
            Assert.That(trie.Search("hello").ToArray(), Is.Empty);
        }

        [Test]
        public void Trie_SearchOnEmpty()
        {
            var trie = new Trie();
            Assert.That(trie.Search("something"), Is.Empty);
        }

        [Test]
        public void Trie_IndexDuplicate()
        {
            var trie = new Trie<int>();
            trie.Index("bonjour", 10);
            trie.Index("bonjour", 20);
            trie.Index("bonsoir", 30);

            Assert.That(trie.Search("bon"), Is.EquivalentTo(new[] { 20, 30 }));
        }

        [Test]
        public void Trie_IndexBetweenSearch()
        {
            var trie = new Trie<int>();
            trie.Index("bonjour", 10);

            Assert.That(trie.Search("bon"), Is.EquivalentTo(new[] { 10 }));

            trie.Index("bonsoir", 20);

            Assert.That(trie.Search("bon"), Is.EquivalentTo(new[] { 10, 20 }));
        }

        [Test, Performance]
        public void Trie_IndexTypesPerfTests()
        {
            var listCache = TypeManager.GetAllTypes().Where(t => t.Type != null).Select(t => t.Type.Name.ToLowerInvariant()).Distinct().ToList();
            var trie = new Trie(TypeManager.GetAllTypes().Where(t => t.Type != null).Select(t => t.Type.Name));

            Measure.Method(() =>
            {
                TypeManager.GetAllTypes().Where(t => t.Type != null).Select(t => t.Type.Name.ToLowerInvariant()).Distinct().ToList();
            })
            .SampleGroup("List indexing")
            .WarmupCount(10)
            .MeasurementCount(100)
            .Run();

            Measure.Method(() =>
            {
                var t = new Trie();
                t.Index(TypeManager.GetAllTypes().Where(info => info.Type != null).Select(info => info.Type.Name));
            })
            .SampleGroup("Trie indexing")
            .WarmupCount(10)
            .MeasurementCount(100)
            .Run();

            Measure.Method(() =>
            {
                var items = listCache.Where(x => x.StartsWith("ro")).ToArray();
            })
            .SampleGroup("Search in List index")
            .WarmupCount(10)
            .MeasurementCount(100)
            .Run();

            Measure.Method(() =>
            {
                var items = trie.Search("ro").ToArray();
            })
            .SampleGroup("Search in Trie index")
            .WarmupCount(10)
            .MeasurementCount(100)
            .Run();
        }

        [Test, Performance]
        public void Trie_ScaleTest()
        {
            var allTypes = new HashSet<string>();
            var allAssemblies = AppDomain.CurrentDomain.GetAssemblies();
            foreach (var assembly in allAssemblies)
            {
                var types = assembly.GetTypes();
                foreach (var type in types)
                {
                    allTypes.Add(type.Name.ToLowerInvariant());
                }
            }

            var step = allTypes.Count / 5;
            for (var length = step; length < allTypes.Count; length += step)
            {
                Measure.Method(() =>
                {
                    var t = new Trie(allTypes.Take(length));
                })
                .SampleGroup($"Indexing {length} / {allTypes.Count}")
                .WarmupCount(1)
                .MeasurementCount(5)
                .Run();
            }
        }

        [Test]
        public void Trie_IndexAllTypesAndExportStatistics()
        {
            var allTypesCount = TypeManager.GetAllTypes().Where(t => t.Type != null).Select(x => x.Type.Name).Distinct().Count();

            var trie = new Trie(TypeManager.GetAllTypes().Where(t => t.Type != null).Select(t => t.Type.Name));

            var stats = trie.GetStatistics();
            Assert.That(stats.nodeCountHavingValue, Is.EqualTo(allTypesCount));

            var maxSubNodeCount = stats.subNodesPerNode.Max();
            UnityEngine.Debug.Log($"Total of {stats.totalNodeCount} nodes, {stats.nodeCountHavingValue} nodes with a value attached, avg of {stats.subNodesPerNode.Average()} children per node, min: {stats.subNodesPerNode.Min()}, max: {maxSubNodeCount}");

            for (var i = 0; i <= maxSubNodeCount; i++)
            {
                UnityEngine.Debug.Log($"{i} children: {stats.subNodesPerNode.Count(x => x == i)}");
            }
        }
    }
}
