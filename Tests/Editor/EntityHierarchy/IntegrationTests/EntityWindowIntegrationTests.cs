using System;
using NUnit.Framework;
using System.Collections;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Editor.Bridge;
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
using ListView = Unity.Editor.Bridge.ListView;
using Object = UnityEngine.Object;

namespace Unity.Entities.Editor.Tests
{
    [TestFixture]
    [SuppressMessage("ReSharper", "Unity.InefficientPropertyAccess")]
    class EntityWindowIntegrationTests
    {
        const string k_AssetsFolderRoot = "Assets";
        const string k_SceneExtension = "unity";

        const string k_SceneName = "DefaultGroupingStrategyTest";
        const string k_SubSceneName = "SubScene";

        string m_TestAssetsDirectory;

        Scene m_Scene;
        SubScene m_SubScene;
        GameObject m_SubSceneRoot;
        bool m_PreviousLiveLinkState;
        World m_PreviousWorld;
        EntityManager m_Manager;
        TestHierarchyHelper m_AssertHelper;

        EntityHierarchyWindow m_Window;
        PlayerLoopSystem m_PreviousPlayerLoop;

        IEnumerator UpdateLiveLink()
        {
            LiveLinkConnection.GlobalDirtyLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility();
        }

        IEnumerator SkipAnEditorFrameAndDiffingUtility()
        {
            yield return SkipAnEditorFrame();
            m_Window.Update();
        }

        static IEnumerator SkipAnEditorFrame()
        {
            EditorUpdateUtility.EditModeQueuePlayerLoopUpdate();

            // Yield twice to ensure EditorApplication.update was invoked before resuming.
            yield return null;
            yield return null;
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            string path;
            do
            {
                path = Path.GetRandomFileName();
            }
            while (AssetDatabase.IsValidFolder(Path.Combine(k_AssetsFolderRoot, path)));

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

            Object.DestroyImmediate(targetGO);
            EditorSceneManager.SaveScene(m_Scene);
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            Object.DestroyImmediate(m_SubSceneRoot);
            AssetDatabase.DeleteAsset(m_TestAssetsDirectory);
            SceneWithBuildConfigurationGUIDs.ClearBuildSettingsCache();
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);

            SubSceneInspectorUtility.LiveLinkEnabledInEditMode = m_PreviousLiveLinkState;
        }

        [SetUp]
        public virtual void Setup()
        {
            m_PreviousPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();

            m_PreviousWorld = World.DefaultGameObjectInjectionWorld;
            DefaultWorldInitialization.Initialize("Test World", true);
            m_Manager = World.DefaultGameObjectInjectionWorld.EntityManager;

            m_Window = CreateWindow();
            m_AssertHelper = new TestHierarchyHelper(m_Window.State);

            World.DefaultGameObjectInjectionWorld.GetOrCreateSystem<SceneSystem>().LoadSceneAsync(m_SubScene.SceneGUID, new SceneSystem.LoadParameters
            {
                Flags = SceneLoadFlags.BlockOnImport | SceneLoadFlags.BlockOnStreamIn
            });

            World.DefaultGameObjectInjectionWorld.Update();
        }

        [TearDown]
        public void TearDown()
        {
            DestroyWindow(m_Window);
            m_AssertHelper = null;

            World.DefaultGameObjectInjectionWorld.Dispose();
            World.DefaultGameObjectInjectionWorld = m_PreviousWorld;
            m_PreviousWorld = null;
            m_Manager = default;

            PlayerLoop.SetPlayerLoop(m_PreviousPlayerLoop);

            TearDownSubScene();
        }

        void TearDownSubScene()
        {
            foreach (GameObject rootGO in m_SubScene.EditingScene.GetRootGameObjects())
                Object.DestroyImmediate(rootGO);
        }

        [UnityTest]
        public IEnumerator TestSetup_ProducesExpectedResult()
        {
            // Initial setup is clean and all is as expected
            Assert.That(m_SubScene.name, Is.EqualTo(k_SubSceneName));
            Assert.That(m_SubScene.SceneName, Is.EqualTo(k_SubSceneName));
            Assert.That(m_SubScene.CanBeLoaded(), Is.True);
            Assert.That(m_SubScene.IsLoaded, Is.True);
            Assert.That(m_SubScene.EditingScene.isLoaded, Is.True);
            Assert.That(m_SubScene.EditingScene.isSubScene, Is.True);

            Assert.That(m_SubSceneRoot.name, Is.EqualTo(k_SubSceneName));
            Assert.That(m_SubSceneRoot, Is.EqualTo(m_SubScene.gameObject));

            Assert.That(m_SubSceneRoot.GetComponent<SubScene>(), Is.Not.Null);
            Assert.That(m_SubSceneRoot.transform.childCount, Is.EqualTo(0));

            Assert.That(m_Scene.rootCount, Is.EqualTo(1));
            Assert.That(m_SubScene.EditingScene.rootCount, Is.EqualTo(0));

            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            SceneManager.MoveGameObjectToScene(go, m_SubScene.EditingScene);

            Assert.That(m_SubScene.EditingScene.rootCount, Is.EqualTo(1));
            Assert.That(m_SubScene.EditingScene.GetRootGameObjects()[0], Is.EqualTo(go));
            Assert.That(go.scene, Is.EqualTo(m_SubScene.EditingScene));

            // Parenting into a SubScene
            var childGO = new GameObject("childGO");
            Assert.That(childGO.scene, Is.EqualTo(m_Scene));

            childGO.transform.parent = go.transform;
            Assert.That(childGO.scene, Is.EqualTo(m_SubScene.EditingScene));

            // Expected Entities: 1. WorldTime - 2. SubScene - 3. SceneSection
            Assert.That(m_Manager.UniversalQuery.CalculateEntityCount(), Is.EqualTo(3));

            yield return UpdateLiveLink();

            Assert.That(m_Window.World, Is.EqualTo(World.DefaultGameObjectInjectionWorld));

            // Expected Entities: 1. WorldTime - 2. SubScene - 3. SceneSection - 4. Converted `go` - 5. Converted `childGO`
            Assert.That(m_Manager.UniversalQuery.CalculateEntityCount(), Is.EqualTo(5));

            // TearDown properly cleans-up the SubScene
            TearDownSubScene();

            Assert.That(m_SubScene.EditingScene.rootCount, Is.EqualTo(0));

            yield return UpdateLiveLink();

            // Expected Entities: 1. WorldTime - 2. SubScene - 3. SceneSection
            Assert.That(m_Manager.UniversalQuery.CalculateEntityCount(), Is.EqualTo(3));
        }

        [UnityTest]
        public IEnumerator BasicSubsceneBasedParenting_ProducesExpectedResult()
        {
            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            SceneManager.MoveGameObjectToScene(go, m_SubScene.EditingScene);

            yield return UpdateLiveLink();

            var (expectedHierarchy, subScene, _, entityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, entityId, 0)); // "go" Converted Entity

            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            // Asserts that the names of the scenes were correctly found.
            using (var rootChildren = m_Window.State.GetChildren(EntityHierarchyNodeId.Root, Allocator.Temp))
            {
                var sceneNode = rootChildren.Single(child => child.Kind == NodeKind.Scene);
                Assert.That(m_Window.State.GetNodeName(sceneNode), Is.EqualTo(k_SceneName));

                using (var sceneChildren = m_Window.State.GetChildren(sceneNode, Allocator.Temp))
                {
                    // Only Expecting a single child here
                    var subsceneNode = sceneChildren[0];
                    Assert.That(m_Window.State.GetNodeName(subsceneNode), Is.EqualTo(k_SubSceneName));
                }
            }
        }

        // Scenario:
        // - Add GameObject to root of subscene
        // - Add second GameObject to root of subscene
        // - Parent second GameObject to first
        // - Unparent second GameObject from first
        // - Delete second GameObject
        [UnityTest]
        public IEnumerator SubsceneBasedParenting_Scenario1()
        {
            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            SceneManager.MoveGameObjectToScene(go, m_SubScene.EditingScene);
            yield return UpdateLiveLink();

            // Adding a second GameObject to a SubScene
            var go2 = new GameObject("go2");
            SceneManager.MoveGameObjectToScene(go2, m_SubScene.EditingScene);
            yield return UpdateLiveLink();

            var (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0),// "go" Converted Entity
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0)); // "go2" Converted Entity
            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            // Parent second GameObject to first
            go2.transform.parent = go.transform;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0))    // "go" Converted Entity
                        .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0)); // "go2" Converted Entity
            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            // Unparent second GameObject from first
            go2.transform.parent = null;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0),// "go" Converted Entity
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0)); // "go2" Converted Entity
            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            // Delete second GameObject
            Object.DestroyImmediate(go2);
            yield return UpdateLiveLink();

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0)); // "go" Converted Entity

            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());
        }

        // Scenario:
        // 3 GameObjects [A, B, C] at the Root of a SubScene
        // Move A into B
        // Move A into C
        // Move B into C
        // Move A back to Root
        // Move B back to Root
        [UnityTest]
        public IEnumerator SubsceneBasedParenting_Scenario2()
        {
            var a = new GameObject("A");
            SceneManager.MoveGameObjectToScene(a, m_SubScene.EditingScene);
            yield return UpdateLiveLink();

            var b = new GameObject("B");
            SceneManager.MoveGameObjectToScene(b, m_SubScene.EditingScene);
            yield return UpdateLiveLink();

            var c = new GameObject("C");
            SceneManager.MoveGameObjectToScene(c, m_SubScene.EditingScene);
            yield return UpdateLiveLink();

            var (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0),
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0),
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            a.transform.parent = b.transform;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene
               .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0))
                    .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0));
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            a.transform.parent = c.transform;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0));
            subScene
               .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0))
                    .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            b.transform.parent = c.transform;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0))
                    .AddChildren(
                         new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0),
                         new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            a.transform.parent = null;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++, 0));
            subScene
               .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0))
                    .AddChild(new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());

            b.transform.parent = null;
            yield return UpdateLiveLink();
            yield return SkipAnEditorFrameAndDiffingUtility(); // Ensuring that all parenting phases have completed

            (expectedHierarchy, subScene, _, nextEntityId) = CreateBaseHierarchyForSubscene();
            subScene.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0),
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId++ ,0),
                new EntityHierarchyNodeId(NodeKind.Entity, nextEntityId, 0));
            m_AssertHelper.AssertHierarchyByKind(expectedHierarchy.Build());
        }

        [UnityTest]
        public IEnumerator Search_ToggleSearchView()
        {
            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            var other = new GameObject("other");
            SceneManager.MoveGameObjectToScene(go, m_SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(other, m_SubScene.EditingScene);

            yield return UpdateLiveLink();

            var searchField = WindowRoot.Q<SearchElement>();
            searchField.Search("go");

            yield return null;

            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.FullViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.SearchViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));

            searchField.Search(string.Empty);

            yield return null;

            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.FullViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.SearchViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.None));
        }

        [UnityTest]
        public IEnumerator Search_DisableSearchWhenSearchElementIsHidden()
        {
            // Adding a GameObject to a SubScene
            var go = new GameObject("go");
            var other = new GameObject("other");
            SceneManager.MoveGameObjectToScene(go, m_SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(other, m_SubScene.EditingScene);

            yield return UpdateLiveLink();

            var searchField = WindowRoot.Q<SearchElement>();
            searchField.Search("go");

            yield return null;

            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.FullViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.SearchViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            var listView = WindowRoot.Q<ListView>(name: Constants.EntityHierarchy.SearchViewName);
            Assert.That(listView.itemsSource
                            .Cast<EntityHierarchyItem>()
                            .Select(x => x.CachedName), Is.EquivalentTo(new[] { k_SubSceneName, "go" }));

            var searchIcon = WindowRoot.Q(className: UssClasses.DotsEditorCommon.SearchIcon);
            m_Window.SendEvent(new Event
            {
                type = EventType.MouseUp,
                mousePosition = searchIcon.worldBound.position
            });

            yield return null;

            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.FullViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.SearchViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.None));

            m_Window.SendEvent(new Event
            {
                type = EventType.MouseUp,
                mousePosition = searchIcon.worldBound.position
            });

            yield return null;

            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.FullViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.SearchViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(listView.itemsSource
                            .Cast<EntityHierarchyItem>()
                            .Select(x => x.CachedName), Is.EquivalentTo(new[] { k_SubSceneName, "go" }));
        }

        [UnityTest]
        public IEnumerator Search_NameSearch()
        {
            // Adding a GameObject to a SubScene
            SceneManager.MoveGameObjectToScene(new GameObject("go"), m_SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("go2"), m_SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("somethingelse"), m_SubScene.EditingScene);

            yield return UpdateLiveLink();

            var searchField = WindowRoot.Q<SearchElement>();
            searchField.Search("go");

            yield return null;

            var listView = WindowRoot.Q<ListView>(name: Constants.EntityHierarchy.SearchViewName);
            var items = listView.itemsSource.Cast<EntityHierarchyItem>().Select(x => x.CachedName);
            Assert.That(items, Is.EquivalentTo(new[] { k_SubSceneName, "go", "go2" }));
        }

        [UnityTest]
        public IEnumerator Search_QuerySearch()
        {
            // Adding a GameObject to a SubScene
            SceneManager.MoveGameObjectToScene(new GameObject("go"), m_SubScene.EditingScene);

            yield return UpdateLiveLink();

            var searchField = WindowRoot.Q<SearchElement>();
            searchField.Search($"c:{nameof(WorldTime)}");

            yield return null;

            var listView = WindowRoot.Q<ListView>(name: Constants.EntityHierarchy.SearchViewName);
            var items = listView.itemsSource.Cast<EntityHierarchyItem>().Select(x => x.CachedName);
            Assert.That(items, Is.EquivalentTo(new[] { nameof(WorldTime) }));
        }

        [UnityTest]
        public IEnumerator Search_QueryAndNameSearch()
        {
            // Adding a GameObject to a SubScene
            SceneManager.MoveGameObjectToScene(new GameObject("abc"), m_SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("def"), m_SubScene.EditingScene);
            SceneManager.MoveGameObjectToScene(new GameObject("ghi"), m_SubScene.EditingScene);

            yield return UpdateLiveLink();

            var searchField = WindowRoot.Q<SearchElement>();
            searchField.Search($"c:{nameof(EntityGuid)} abc");

            yield return null;

            var listView = WindowRoot.Q<ListView>(name: Constants.EntityHierarchy.SearchViewName);
            var items = listView.itemsSource.Cast<EntityHierarchyItem>().Select(x => x.CachedName);
            Assert.That(items, Is.EquivalentTo(new[] { k_SubSceneName, "abc" }));
        }

        [UnityTest]
        public IEnumerator Search_NameSearch_NoResult()
        {
            SceneManager.MoveGameObjectToScene(new GameObject("go"), m_SubScene.EditingScene);

            yield return UpdateLiveLink();

            var searchField = WindowRoot.Q<SearchElement>();
            searchField.Search("hello");

            yield return null;

            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.FullViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.None));
            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.SearchViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.None));

            var centeredMessageElement = WindowRoot.Q<CenteredMessageElement>();
            Assert.That(centeredMessageElement, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(centeredMessageElement.Title, Is.EqualTo(EntityHierarchy.NoEntitiesFoundTitle));
            Assert.That(centeredMessageElement.Message, Is.Empty);
        }

        [UnityTest]
        public IEnumerator Search_QuerySearch_NoResult()
        {
            SceneManager.MoveGameObjectToScene(new GameObject("go"), m_SubScene.EditingScene);

            yield return UpdateLiveLink();

            var searchField = WindowRoot.Q<SearchElement>();
            searchField.Search("c:TypeThatDoesntExist");

            yield return null;

            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.FullViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(WindowRoot.Q(name: Constants.EntityHierarchy.SearchViewName), UIToolkitTestHelper.Is.Display(DisplayStyle.None));

            var centeredMessageElement = WindowRoot.Q<CenteredMessageElement>();
            Assert.That(centeredMessageElement, UIToolkitTestHelper.Is.Display(DisplayStyle.Flex));
            Assert.That(centeredMessageElement.Title, Is.EqualTo(EntityHierarchy.ComponentTypeNotFoundTitle));
            Assert.That(centeredMessageElement.Message, Is.EqualTo(string.Format(EntityHierarchy.ComponentTypeNotFoundContent, "TypeThatDoesntExist")));
        }

        [UnityTest]
        public IEnumerator Search_QuerySearch_IncludeSpecialEntity([Values(typeof(Prefab), typeof(Disabled))] Type componentType)
        {
            SceneManager.MoveGameObjectToScene(new GameObject("go"), m_SubScene.EditingScene);

            yield return UpdateLiveLink();

            var e = m_Manager.CreateEntity();
            m_Manager.SetName(e, "Test entity");
            m_Manager.AddComponent<EntityGuid>(e);
            m_Manager.AddComponent(e, componentType);

            yield return SkipAnEditorFrameAndDiffingUtility();

            var treeview = WindowRoot.Q<TreeView>(Constants.EntityHierarchy.FullViewName);
            var expectedNode = EntityHierarchyNodeId.FromEntity(e);
            Assert.That(treeview.items.Cast<EntityHierarchyItem>().Select(i => i.NodeId), Does.Contain(expectedNode));

            var searchField = WindowRoot.Q<SearchElement>();
            searchField.Search($"c:{typeof(EntityGuid).FullName}");

            yield return SkipAnEditorFrameAndDiffingUtility();

            var listview = WindowRoot.Q<ListView>(Constants.EntityHierarchy.SearchViewName);
            Assert.That(listview.itemsSource.Cast<EntityHierarchyItem>().Select(i => i.NodeId), Does.Contain(expectedNode));
        }

        [UnityTest]
        public IEnumerator Selection_DestroyingSelectedEntityDeselectInView()
        {
            var internalListView = WindowRoot.Q<TreeView>(Constants.EntityHierarchy.FullViewName).Q<ListView>();

            var e = m_Manager.CreateEntity();
            var node = EntityHierarchyNodeId.FromEntity(e);

            yield return SkipAnEditorFrameAndDiffingUtility();

            Assert.That(internalListView.currentSelectionIds, Is.Empty);

            EntitySelectionProxy.SelectEntity(m_Manager.World, e);
            yield return null;

            Assert.That(internalListView.currentSelectionIds, Is.EquivalentTo(new[] { node.GetHashCode() }));

            m_Manager.DestroyEntity(e);
            yield return SkipAnEditorFrameAndDiffingUtility();

            Assert.That(internalListView.selectedItems, Is.Empty);
        }

        // Creates the basic hierarchy for a single scene with a single subscene.
        static (TestHierarchy.TestNode root, TestHierarchy.TestNode subScene, int nextSceneId, int nextEntityId) CreateBaseHierarchyForSubscene()
        {
            var entityId = 0;
            var sceneId = 0;

            var rootNode = TestHierarchy.CreateRoot();

            rootNode.AddChildren(
                new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),                                  // World Time Entity
                new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0),                                  // SubScene Entity
                new EntityHierarchyNodeId(NodeKind.Entity, entityId++, 0));                                 // SceneSection Entity

            var subSceneNode =
                rootNode.AddChild(EntityHierarchyNodeId.FromScene(sceneId++))                  // Main Scene
                                        .AddChild(EntityHierarchyNodeId.FromSubScene(sceneId++)); // SubScene

            return (rootNode, subSceneNode, sceneId, entityId);
        }

        static EntityHierarchyWindow CreateWindow()
        {
            var window = ScriptableObject.CreateInstance<EntityHierarchyWindow>();
            window.DisableDifferCooldownPeriod = true;
            window.Show();
            window.Update();
            window.ChangeCurrentWorld(World.DefaultGameObjectInjectionWorld);

            Assert.That(window.World, Is.EqualTo(World.DefaultGameObjectInjectionWorld));

            return window;
        }

        static void DestroyWindow(EntityHierarchyWindow window)
        {
            window.Close();
            Object.DestroyImmediate(window);
        }

        VisualElement WindowRoot => m_Window.rootVisualElement;
    }
}
