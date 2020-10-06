using System;
using NUnit.Framework;
using System.Linq;
using Unity.Entities;

namespace Unity.Entities.Editor.Tests
{
    class ComponentTypeCacheTests
    {
        [Test]
        public void ComponentTypeCache_ExactMatching()
        {
            var type1 = typeof(Unity.Tests.NamespaceA.ComponentTypeCacheTest);
            var type2 = typeof(Unity.Tests.NamespaceB.ComponentTypeCacheTest);
            var type3 = typeof(global::ComponentTypeCacheTest);

            Assert.That(TypeManager.GetAllTypes().SingleOrDefault(t => t.Type == type1), Is.Not.Null, $"Type {type1} must be present in {nameof(TypeManager)}");
            Assert.That(TypeManager.GetAllTypes().SingleOrDefault(t => t.Type == type2), Is.Not.Null, $"Type {type2} must be present in {nameof(TypeManager)}");
            Assert.That(TypeManager.GetAllTypes().SingleOrDefault(t => t.Type == type3), Is.Not.Null, $"Type {type3} must be present in {nameof(TypeManager)}");

            var rotationTypes = ComponentTypeCache.GetExactMatchingTypes("componenttypecachetest").ToArray();
            Assert.That(rotationTypes, Is.EquivalentTo(new[] { type1, type2, type3 }));

            var type = ComponentTypeCache.GetExactMatchingTypes(type1.FullName);
            Assert.That(type, Is.EquivalentTo(new[] { type1 }));

            type = ComponentTypeCache.GetExactMatchingTypes(type2.FullName);
            Assert.That(type, Is.EquivalentTo(new[] { type2 }));

            // Equivalent to searching for "componenttypecachetest" directly.
            // Will match all types named "componenttypecachetest" regardless of the namespace
            type = ComponentTypeCache.GetExactMatchingTypes(type3.FullName);
            Assert.That(type, Is.EquivalentTo(new[] { type1, type2, type3 }));
        }

        [Test]
        public void ComponentTypeCache_FuzzyMatching()
        {
            var type1 = typeof(Unity.Tests.NamespaceA.ComponentTypeCacheTest);
            var type2 = typeof(Unity.Tests.NamespaceB.ComponentTypeCacheTest);
            var type3 = typeof(global::ComponentTypeCacheTest);

            var fuzzyTypes = ComponentTypeCache.GetFuzzyMatchingTypes("ComponentTypeCacheT").ToArray();
            Assert.That(fuzzyTypes, Is.EquivalentTo(new[] { type1, type2, type3 }));

            var type = ComponentTypeCache.GetFuzzyMatchingTypes(type1.FullName);
            Assert.That(type, Is.EquivalentTo(new[] { type1 }));

            type = ComponentTypeCache.GetFuzzyMatchingTypes(type2.FullName);
            Assert.That(type, Is.EquivalentTo(new[] { type2 }));

            type = ComponentTypeCache.GetFuzzyMatchingTypes(type3.FullName);
            Assert.That(type, Is.EquivalentTo(new[] { type1, type2, type3 }));
        }
    }
}

namespace Unity.Tests.NamespaceA
{
    struct ComponentTypeCacheTest : IComponentData { }
}

namespace Unity.Tests.NamespaceB
{
    struct ComponentTypeCacheTest : IComponentData { }
}

// Global component type for component type cache tests
struct ComponentTypeCacheTest : IComponentData
{
}
