using NUnit.Framework;
using System.Collections;
using System.IO;
using System.Linq;
using Unity.Properties.UI;
using Unity.Scenes;
using Unity.Scenes.Editor;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor.Tests
{
    partial class SystemScheduleWindowIntegrationTests
    {
        World m_DefaultWorld;
        ComponentSystemGroup m_TestSystemGroup;
        ComponentSystem m_TestSystem1;
        ComponentSystem m_TestSystem2;
        SystemScheduleWindow m_SystemScheduleWindow;
        Scene m_Scene;
        SubScene m_SubScene;
        GameObject m_SubSceneRoot;
        bool m_PreviousLiveLinkState;
        const string k_SystemScheduleEditorWorld = "Editor World";
        const string k_SystemScheduleTestWorld = "SystemScheduleTestWorld";
        const string k_AssetsFolderRoot = "Assets";
        const string k_SceneExtension = "unity";
        const string k_SceneName = "SystemsWindowTests";
        const string k_SubSceneName = "SubScene";
        [SerializeField]
        string m_TestAssetsDirectory;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            if (!EditorApplication.isPlaying)
            {
                string path;
                do
                {
                    path = Path.GetRandomFileName();
                } while (AssetDatabase.IsValidFolder(Path.Combine(k_AssetsFolderRoot, path)));

                m_PreviousLiveLinkState = SubSceneInspectorUtility.LiveLinkEnabledInEditMode;
                SubSceneInspectorUtility.LiveLinkEnabledInEditMode = true;

                var guid = AssetDatabase.CreateFolder(k_AssetsFolderRoot, path);
                m_TestAssetsDirectory = AssetDatabase.GUIDToAssetPath(guid);

                m_Scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);
                var mainScenePath = Path.Combine(m_TestAssetsDirectory, $"{k_SceneName}.{k_SceneExtension}");

                EditorSceneManager.SaveScene(m_Scene, mainScenePath);
                SceneManager.SetActiveScene(m_Scene);

                // Temp context GameObject, necessary to create an empty subscene
                var targetGO = new GameObject(k_SubSceneName);

                var subsceneArgs = new SubSceneContextMenu.NewSubSceneArgs(targetGO, m_Scene, SubSceneContextMenu.NewSubSceneMode.EmptyScene);
                m_SubScene = SubSceneContextMenu.CreateNewSubScene(targetGO.name, subsceneArgs, InteractionMode.AutomatedAction);
                m_SubSceneRoot = m_SubScene.gameObject;

                UnityEngine.Object.DestroyImmediate(targetGO);
                EditorSceneManager.SaveScene(m_Scene);
            }
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            UnityEngine.Object.DestroyImmediate(m_SubSceneRoot);
            AssetDatabase.DeleteAsset(m_TestAssetsDirectory);
            SceneWithBuildConfigurationGUIDs.ClearBuildSettingsCache();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);

            SubSceneInspectorUtility.LiveLinkEnabledInEditMode = m_PreviousLiveLinkState;
        }

        [SetUp]
        public void SetUp()
        {
            m_DefaultWorld = World.DefaultGameObjectInjectionWorld;
            m_TestSystemGroup = m_DefaultWorld.GetOrCreateSystem<SystemScheduleTestGroup>();
            m_TestSystem1 = m_DefaultWorld.GetOrCreateSystem<SystemScheduleTestSystem1>();
            m_TestSystem2 = m_DefaultWorld.GetOrCreateSystem<SystemScheduleTestSystem2>();
            m_TestSystemGroup.AddSystemToUpdateList(m_TestSystem1);
            m_TestSystemGroup.AddSystemToUpdateList(m_TestSystem2);
            m_DefaultWorld.GetOrCreateSystem<SimulationSystemGroup>().AddSystemToUpdateList(m_TestSystemGroup);

            m_SystemScheduleWindow = !EditorApplication.isPlaying ? SystemScheduleTestUtilities.CreateSystemsWindow() : EditorWindow.GetWindow<SystemScheduleWindow>();

            m_SystemScheduleWindow.SelectedWorld = m_DefaultWorld;
            m_SystemScheduleWindow.BaseState.SelectedWorldName = k_SystemScheduleEditorWorld;
        }

        [TearDown]
        public void TearDown()
        {
            if (!EditorApplication.isPlaying)
                SystemScheduleTestUtilities.DestroySystemsWindow(m_SystemScheduleWindow);
        }

        [UnityTest]
        public IEnumerator SystemScheduleWindow_SearchForSingleComponent()
        {
            yield return new SystemScheduleTestUtilities.UpdateSystemGraph(typeof(SystemScheduleTestSystem1));
            m_SystemScheduleWindow.rootVisualElement.Q<SearchElement>().Search("c:SystemScheduleTestData1");

            var systemTreeView = m_SystemScheduleWindow.rootVisualElement.Q<SystemTreeView>();
            Assert.That(systemTreeView.m_ListViewFilteredItems.Count, Is.EqualTo(1));
            Assert.That(systemTreeView.m_ListViewFilteredItems.FirstOrDefault()?.Node.Name, Is.EqualTo("System Schedule Test System 1"));
        }

        [UnityTest]
        public IEnumerator SystemScheduleWindow_SearchForSystemName()
        {
            yield return new SystemScheduleTestUtilities.UpdateSystemGraph(typeof(SystemScheduleTestSystem1));
            m_SystemScheduleWindow.rootVisualElement.Q<SearchElement>().Search("SystemScheduleTestSystem1");

            var systemTreeView = m_SystemScheduleWindow.rootVisualElement.Q<SystemTreeView>();
            Assert.That(systemTreeView.m_ListViewFilteredItems.Count, Is.EqualTo(1));
            Assert.That(systemTreeView.m_ListViewFilteredItems.FirstOrDefault()?.Node.Name, Is.EqualTo("System Schedule Test System 1"));
        }

        [Test]
        public void SystemScheduleWindow_SearchForNonExistingSystem()
        {
            m_SystemScheduleWindow.rootVisualElement.Q<SearchElement>().Search("raasdfasd");
            Assert.That(m_SystemScheduleWindow.rootVisualElement.Q<SystemTreeView>().m_ListViewFilteredItems.Count, Is.EqualTo(0));
        }

        [Test]
        public void SystemScheduleWindow_GetSelectedWorld()
        {
            Assert.That(m_SystemScheduleWindow.SelectedWorld.Name, Is.EqualTo(k_SystemScheduleEditorWorld));
        }

        [Test]
        public void SystemScheduleWindow_SearchBuilder_ParseSearchString()
        {
            var searchElement = m_SystemScheduleWindow.rootVisualElement.Q<SearchElement>();
            searchElement.Search("c:Com1 C:Com2 randomName Sd:System1");
            var systemTreeView = m_SystemScheduleWindow.rootVisualElement.Q<SystemTreeView>();
            var parseResult = systemTreeView.SearchFilter;

            Assert.That(parseResult.Input, Is.EqualTo( "c:Com1 C:Com2 randomName Sd:System1" ));
            Assert.That(parseResult.ComponentNames, Is.EquivalentTo(new[] { "Com1", "Com2" }));
            Assert.That(parseResult.DependencySystemNames, Is.EquivalentTo(new[] { "System1" }));
            Assert.That(parseResult.ErrorComponentType, Is.EqualTo( "Com1" ));

            searchElement.Search("c:   com1 C:Com2");
            Assert.That(systemTreeView.SearchFilter.ComponentNames, Is.EquivalentTo(new[] { string.Empty, "Com2"}));
        }

        [Test]
        public void SystemScheduleWindow_SearchBuilder_ParseSearchString_EmptyString()
        {
            m_SystemScheduleWindow.rootVisualElement.Q<SearchElement>().Search(string.Empty);
            var parseResult =  m_SystemScheduleWindow.rootVisualElement.Q<SystemTreeView>().SearchFilter;

            Assert.That(parseResult.Input, Is.EqualTo(string.Empty));
            Assert.That(parseResult.ComponentNames, Is.Empty);
            Assert.That(parseResult.DependencySystemNames, Is.Empty);
            Assert.That(parseResult.ErrorComponentType, Is.EqualTo(string.Empty));
        }

        [Test]
        public void SystemScheduleWindow_ComponentTypeAccessMode()
        {
            using (var query = m_DefaultWorld.EntityManager.CreateEntityQuery(
                ComponentType.ReadOnly<SystemScheduleTestData1>()
                , ComponentType.Exclude<SystemScheduleTestData2>()))
            {
                using (var componentTypePooledList = query.GetQueryTypes().ToPooledList())
                using (var readWriteQueryTypePooledList = query.GetReadAndWriteTypes().ToPooledList())
                {
                    var componentTypeList = componentTypePooledList.List;
                    var readWriteQueryTypeList = readWriteQueryTypePooledList.List;
                    componentTypeList.Sort(EntityQueryUtility.CompareTypes);

                    var index = 0;

                    foreach (var componentType in componentTypeList)
                    {
                        var componentManagedType = componentType.GetManagedType();
                        var componentName = EntityQueryUtility.SpecifiedTypeName(componentManagedType);

                        switch (componentName)
                        {
                            case nameof(SystemScheduleTestData1):
                            {
                                var accessMode1 = SystemDetailsVisualElement.GetAccessMode(componentType, readWriteQueryTypeList);
                                Assert.That(EntityQueryUtility.StyleForAccessMode(accessMode1), Is.EqualTo(UssClasses.SystemScheduleWindow.Detail.ReadOnlyIcon));
                                break;
                            }

                            case nameof(SystemScheduleTestData2):
                            {
                                var accessMode2 = SystemDetailsVisualElement.GetAccessMode(componentType, readWriteQueryTypeList);
                                Assert.That(EntityQueryUtility.StyleForAccessMode(accessMode2), Is.EqualTo(UssClasses.SystemScheduleWindow.Detail.ExcludeIcon));
                                break;
                            }
                        }
                        index++;
                    }
                }
            }
        }

        [Test]
        public void SystemScheduleWindow_ContainsThisComponentType()
        {
            var componentTypesInQuery1 = EntityQueryUtility.CollectComponentTypesFromSystemQuery(m_TestSystem1);
            var typesInQuery1 = componentTypesInQuery1 as string[] ?? componentTypesInQuery1.ToArray();
            Assert.That(typesInQuery1.Contains(nameof(SystemScheduleTestData1)));
            Assert.That(typesInQuery1.Contains(nameof(SystemScheduleTestData2)));

            var componentTypesInQuery2 = EntityQueryUtility.CollectComponentTypesFromSystemQuery(m_TestSystem2);
            var typesInQuery2 = componentTypesInQuery2 as string[] ?? componentTypesInQuery2.ToArray();
            Assert.That(!typesInQuery2.Contains(nameof(SystemScheduleTestData1)));
            Assert.That(!typesInQuery2.Contains(nameof(SystemScheduleTestData2)));
        }

        [UnityTest]
        public IEnumerator SystemScheduleWindow_CustomWorld()
        {
            var world = new World(k_SystemScheduleTestWorld);

            m_TestSystem1 = world.GetOrCreateSystem<SystemScheduleTestSystem1>();
            m_TestSystem2 = world.GetOrCreateSystem<SystemScheduleTestSystem2>();
            world.GetOrCreateSystem<SimulationSystemGroup>().AddSystemToUpdateList(m_TestSystem1);
            world.GetOrCreateSystem<SimulationSystemGroup>().AddSystemToUpdateList(m_TestSystem2);

            var m_PreviousPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();

            var playerLoop = PlayerLoop.GetDefaultPlayerLoop();
            ScriptBehaviourUpdateOrder.AddWorldToPlayerLoop(world, ref playerLoop);
            PlayerLoop.SetPlayerLoop(playerLoop);

            m_SystemScheduleWindow.SelectedWorld = world;
            m_SystemScheduleWindow.BaseState.SelectedWorldName = k_SystemScheduleTestWorld;

            Assert.That(m_SystemScheduleWindow.SelectedWorld.Name, Is.EqualTo(k_SystemScheduleTestWorld));

            yield return new SystemScheduleTestUtilities.UpdateSystemGraph(typeof(SystemScheduleTestSystem1));
            Assert.That( m_SystemScheduleWindow.rootVisualElement.Q<SystemTreeView>().CheckIfTreeViewContainsGivenSystemType(typeof(SystemScheduleTestSystem1)), Is.True);

            world.Dispose();
            m_SystemScheduleWindow.Update();
            Assert.That(m_SystemScheduleWindow.SelectedWorld.Name, Is.EqualTo(k_SystemScheduleEditorWorld));

            if (world != null && world.IsCreated)
                world.Dispose();

            PlayerLoop.SetPlayerLoop(m_PreviousPlayerLoop);
        }
    }
}
