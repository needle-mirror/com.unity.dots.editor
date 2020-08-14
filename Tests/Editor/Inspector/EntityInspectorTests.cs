#pragma warning disable 649
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor.tests
{
    using EntityInspectorTypes;

    namespace EntityInspectorTypes
    {
        class InspectorTestWindow : EditorWindow
        {
            public void AddRoot(VisualElement element)
            {
                rootVisualElement.Add(element);
            }
        }

        struct TagComponent : IComponentData
        {
        }

        struct StructComponent : IComponentData
        {
            public float Value;
        }

        class ClassComponent : IComponentData
        {
            public float Value;
        }

        struct SharedStructComponent : ISharedComponentData
        {
            public float Value;
        }
    }

    [TestFixture]
    class EntityInspectorTests
    {
        World m_World;
        Entity m_Entity;
        InspectorSettings.InspectorBackend m_PreviousBackend;
        UnityEditor.Editor m_Editor;
        EntitySelectionProxy m_Proxy;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            m_World = new World("Entity Inspector tests");
            m_PreviousBackend = InspectorUtility.Settings.InternalBackend;
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            m_World.Dispose();
            InspectorUtility.Settings.InternalBackend = m_PreviousBackend;
        }

        [SetUp]
        public void SetUp()
        {
            InspectorUtility.Settings.InternalBackend = InspectorSettings.InspectorBackend.Normal;

            m_Entity = m_World.EntityManager.CreateEntity();

            m_Proxy = ScriptableObject.CreateInstance<EntitySelectionProxy>();
            m_Proxy.SetEntity(m_World, m_Entity);
            m_Editor = UnityEditor.Editor.CreateEditor(m_Proxy);
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(m_Proxy);
            Object.DestroyImmediate(m_Editor);
            m_World.EntityManager.DestroyEntity(m_Entity);
        }

        [Test]
        [TestCase(InspectorSettings.InspectorBackend.Debug, false)]
        [TestCase(InspectorSettings.InspectorBackend.Normal, true)]
        public void CustomEditor_WhenSettingIsSet_CanBeOverriden(InspectorSettings.InspectorBackend mode,
            bool shouldBeUsed)
        {
            InspectorUtility.Settings.InternalBackend = mode;
            var editor = UnityEditor.Editor.CreateEditor(m_Proxy);
            try
            {
                if (shouldBeUsed)
                    Assert.That(editor, Is.TypeOf<EntityEditor>());
                else
                    Assert.That(editor, !Is.TypeOf<EntityEditor>());
            }
            finally
            {
                Object.DestroyImmediate(editor);
            }
        }

        [Test]
        public void Entity_WithNoComponents_HasEmptyInspector()
        {
            Assert.That(m_Editor, Is.TypeOf<EntityEditor>());
            var root = m_Editor.CreateInspectorGUI();
            var list = root.Query<ComponentElementBase>().ToList();
            Assert.That(list.Count, Is.EqualTo(0));
        }

        [Test]
        public void Entity_WithComponents_HasMatchingComponentElements()
        {
            Assert.That(m_Editor, Is.TypeOf<EntityEditor>());

            m_World.EntityManager.AddComponent<TagComponent>(m_Entity);
            m_World.EntityManager.AddComponent<StructComponent>(m_Entity);
            m_World.EntityManager.AddComponent<ClassComponent>(m_Entity);
            m_World.EntityManager.AddSharedComponentData(m_Entity, new SharedStructComponent());

            var root = m_Editor.CreateInspectorGUI();
            var query = root.Query<ComponentElementBase>();
            var list = query.ToList();
            Assert.That(list.Count, Is.EqualTo(4));
            Assert.That(list.OfType<TagElement<TagComponent>>().Count(), Is.EqualTo(1));
            Assert.That(list.OfType<ComponentElement<StructComponent>>().Count(), Is.EqualTo(1));
            Assert.That(list.OfType<ComponentElement<ClassComponent>>().Count(), Is.EqualTo(1));
            Assert.That(list.OfType<ComponentElement<SharedStructComponent>>().Count(), Is.EqualTo(1));
            Is.EqualTo(1);
        }

        [Test]
        public void Inspector_WhenComponentAreAddedOrRemoved_UpdatesProperly()
        {
            Assert.That(m_Editor, Is.TypeOf<EntityEditor>());
            var root = m_Editor.CreateInspectorGUI();
            var list = new List<ComponentElementBase>();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(0));
            list.Clear();

            m_World.EntityManager.AddComponent<TagComponent>(m_Entity);
            root.ForceUpdateBindings();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list.OfType<TagElement<TagComponent>>().Count(), Is.EqualTo(1));
            list.Clear();

            m_World.EntityManager.AddComponent<StructComponent>(m_Entity);
            root.ForceUpdateBindings();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(2));
            Assert.That(list.OfType<TagElement<TagComponent>>().Count(), Is.EqualTo(1));
            Assert.That(list.OfType<ComponentElement<StructComponent>>().Count(), Is.EqualTo(1));
            list.Clear();

            m_World.EntityManager.RemoveComponent<TagComponent>(m_Entity);
            root.ForceUpdateBindings();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(1));
            Assert.That(list.OfType<ComponentElement<StructComponent>>().Count(), Is.EqualTo(1));
            list.Clear();

            m_World.EntityManager.RemoveComponent<StructComponent>(m_Entity);
            root.ForceUpdateBindings();
            root.Query<ComponentElementBase>().ToList(list);
            Assert.That(list.Count, Is.EqualTo(0));
            list.Clear();
        }

        [Test]
        public void Inspector_WhenComponentValueIsUpdated_UpdatesProperly()
        {
            Assert.That(m_Editor, Is.TypeOf<EntityEditor>());
            var entityEditor = (EntityEditor) m_Editor;
            m_World.EntityManager.AddComponent<StructComponent>(m_Entity);
            var root = m_Editor.CreateInspectorGUI();
            // Needed to get the events fired up.
            var window = ScriptableObject.CreateInstance<InspectorTestWindow>();
            window.Show();
            window.AddRoot(root);
            try
            {
                var element = root.Q<ComponentElement<StructComponent>>();
                var field = element.Q<FloatField>();
                Assert.That(field.value, Is.EqualTo(0.0f));

                m_World.EntityManager.SetComponentData(m_Entity, new StructComponent {Value = 15.0f});
                root.ForceUpdateBindings();
                Assert.That(field.value, Is.EqualTo(15.0f));

                field.value = 100.0f;
                var data = m_World.EntityManager.GetComponentData<StructComponent>(m_Entity);
                root.ForceUpdateBindings();
                if (entityEditor.m_Context.IsReadOnly)
                {
                    Assert.That(data.Value, Is.EqualTo(15.0f));
                    Assert.That(field.value, Is.EqualTo(15.0f));
                }
                else
                {
                    Assert.That(data.Value, Is.EqualTo(100.0f));
                    Assert.That(field.value, Is.EqualTo(100.0f));
                }
            }
            finally
            {
                window.Close();
            }
        }
    }
}
#pragma warning restore 649
