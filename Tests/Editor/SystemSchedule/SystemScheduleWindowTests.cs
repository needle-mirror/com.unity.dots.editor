using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.LowLevel;

namespace Unity.Entities.Editor.Tests
{
    class SystemScheduleWindowTests
    {
        World m_World;
        PlayerLoopSystem m_PreviousPlayerLoop;
        ComponentSystem m_TestSystem1;
        ComponentSystem m_TestSystem2;
        SystemScheduleWindow m_SystemScheduleWindow;
        SystemScheduleTreeView m_SystemTreeView;

        const string k_SystemScheduleTestWorld = "SystemScheduleTestWorld";
        const string k_SystemScheduleEditorWorld = "Editor World";

        static void CloseAllSystemScheduleWindow()
        {
            var windows = UnityEngine.Resources.FindObjectsOfTypeAll<SystemScheduleWindow>();
            foreach (var window in windows)
            {
                if (window != null)
                    window.Close();
            }
        }

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            CloseAllSystemScheduleWindow();
            m_SystemScheduleWindow = EditorWindow.GetWindow<SystemScheduleWindow>();
        }

        [OneTimeTearDown]
        public void OneTimeTearDown()
        {
            CloseAllSystemScheduleWindow();
        }

        [SetUp]
        public void SetUp()
        {
            m_World = new World(k_SystemScheduleTestWorld);

            m_TestSystem1 = m_World.GetOrCreateSystem<SystemScheduleTestSystem1>();
            m_TestSystem2 = m_World.GetOrCreateSystem<SystemScheduleTestSystem2>();
            m_World.GetOrCreateSystem<SimulationSystemGroup>().AddSystemToUpdateList(m_TestSystem1);
            m_World.GetOrCreateSystem<SimulationSystemGroup>().AddSystemToUpdateList(m_TestSystem2);

            m_PreviousPlayerLoop = PlayerLoop.GetCurrentPlayerLoop();

            var playerLoop = PlayerLoop.GetDefaultPlayerLoop();
            ScriptBehaviourUpdateOrder.AddWorldToPlayerLoop(m_World, ref playerLoop);
            PlayerLoop.SetPlayerLoop(playerLoop);

            m_SystemScheduleWindow.SelectedWorld = m_World;
            m_SystemScheduleWindow.BaseState.SelectedWorldName = k_SystemScheduleTestWorld;
        }

        [TearDown]
        public void TearDown()
        {
            if (m_World != null && m_World.IsCreated)
                m_World.Dispose();

            PlayerLoop.SetPlayerLoop(m_PreviousPlayerLoop);
        }

        [Test]
        public void SystemScheduleWindow_GetSelectedWorld()
        {
            Assert.That(m_SystemScheduleWindow.SelectedWorld.Name, Is.EqualTo(k_SystemScheduleTestWorld));
        }

        [Test]
        public void SystemScheduleWindow_ComponentTypeAccessMode()
        {
            using (var query = m_World.EntityManager.CreateEntityQuery(
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
        public void SystemScheduleWindow_SystemDependency()
        {
            var dependencyList = new List<Type>();
            var systemTypeList = new List<Type> { m_TestSystem1.GetType() };
            SystemScheduleUtilities.GetSystemDepListFromSystemTypes(systemTypeList, dependencyList);

            Assert.That(dependencyList.Count, Is.EqualTo(2));
            Assert.That(dependencyList[0].Name, Is.EqualTo(nameof(SystemScheduleTestSystem2)));
            Assert.That(dependencyList[1].Name, Is.EqualTo(nameof(SystemScheduleTestSystem1)));
        }

        [Test]
        public void SystemScheduleWindow_ContainsThisComponentType()
        {
            Assert.That(EntityQueryUtility.ContainsThisComponentType(m_TestSystem1, nameof(SystemScheduleTestData1)), Is.EqualTo(true));
            Assert.That(EntityQueryUtility.ContainsThisComponentType(m_TestSystem2, nameof(SystemScheduleTestData2)), Is.EqualTo(false));
        }

        [Test]
        public void SystemScheduleWindow_ContainsGivenSystemType()
        {
            m_SystemScheduleWindow.m_SystemTreeView.Refresh(m_World);
            while (m_SystemScheduleWindow.m_SystemTreeView.m_TreeRootItems.Count == 0)
            {
                PlayerLoopSystemGraph.ValidateCurrentGraph();
            }

            Assert.That(m_SystemScheduleWindow.m_SystemTreeView, Is.Not.EqualTo(null));
            Assert.That(m_SystemScheduleWindow.m_SystemTreeView.CheckIfTreeViewContainsGivenSystemType<SystemScheduleTestSystem1>(), Is.True);
            Assert.That(m_SystemScheduleWindow.m_SystemTreeView.CheckIfTreeViewContainsGivenSystemType<SystemScheduleTestSystem2>(), Is.True);
        }

        [Test]
        public void SystemScheduleWindow_DestroyCurrentWorld()
        {
            m_World.Dispose();
            m_SystemScheduleWindow.Update();
            Assert.That(m_SystemScheduleWindow.SelectedWorld.Name, Is.EqualTo(k_SystemScheduleEditorWorld));
        }

        [Test]
        public void SystemScheduleWindow_SearchFilter()
        {
            m_SystemScheduleWindow.m_SystemTreeView.SearchFilter = SystemScheduleSearchBuilder.ParseSearchString("TestSystem1");
            m_SystemScheduleWindow.m_SystemTreeView.Refresh(m_World);
            while (m_SystemScheduleWindow.m_SystemTreeView.m_TreeRootItems.Count == 0)
            {
                PlayerLoopSystemGraph.ValidateCurrentGraph();
            }

            Assert.That(m_SystemScheduleWindow.m_SystemTreeView.CheckIfTreeViewContainsGivenSystemType<SystemScheduleTestSystem1>(), Is.EqualTo(true));
            Assert.That(m_SystemScheduleWindow.m_SystemTreeView.CheckIfTreeViewContainsGivenSystemType<SystemScheduleTestSystem2>(), Is.EqualTo(false));

            m_SystemScheduleWindow.m_SystemTreeView.SearchFilter = SystemScheduleSearchBuilder.ParseSearchString("bonjour");
            m_SystemScheduleWindow.m_SystemTreeView.Refresh(m_World);
            while (m_SystemScheduleWindow.m_SystemTreeView.m_TreeRootItems.Count == 0)
            {
                PlayerLoopSystemGraph.ValidateCurrentGraph();
            }

            Assert.That(m_SystemScheduleWindow.m_SystemTreeView.CheckIfTreeViewContainsGivenSystemType<SystemScheduleTestSystem1>(), Is.EqualTo(false));
            Assert.That(m_SystemScheduleWindow.m_SystemTreeView.CheckIfTreeViewContainsGivenSystemType<SystemScheduleTestSystem2>(), Is.EqualTo(false));
        }

        [Test]
        public void SystemScheduleWindow_SearchBuilder()
        {
            var searchString = "c: Com1 C:Com2 Sd:System1 randomName";
            var parseResult = SystemScheduleSearchBuilder.ParseSearchString(searchString);

            Assert.That(parseResult.Input, Is.EqualTo(searchString));
            Assert.That(parseResult.ComponentNames.ElementAt(0), Is.EqualTo("Com1"));
            Assert.That(parseResult.ComponentNames.ElementAt(1), Is.EqualTo("Com2"));
            Assert.That(parseResult.DependencySystemNames.ElementAt(0), Is.EqualTo("System1"));
            Assert.That(parseResult.SystemNames.ElementAt(0), Is.EqualTo("randomName"));

            searchString = "c:   com";
            parseResult = SystemScheduleSearchBuilder.ParseSearchString(searchString);
            Assert.That(parseResult.ComponentNames.ElementAt(0), Is.EqualTo(string.Empty));
        }
    }

    static class SystemScheduleTreeViewExtension
    {
        public static bool CheckIfTreeViewContainsGivenSystemType<T>(this SystemScheduleTreeView @this)
        {
            var systemName = typeof(T).Name;

            foreach (var rootItem in @this.m_TreeRootItems)
            {
                if (!(rootItem is SystemTreeViewItem systemTreeViewItem))
                    return false;

                if (CheckIfTreeViewItemContainsSystem(systemTreeViewItem, systemName))
                    return true;
            }

            return false;
        }

        static bool CheckIfTreeViewItemContainsSystem(SystemTreeViewItem item, string systemName)
        {
            if (item.children.Any())
            {
                foreach (var childItem in item.children)
                {
                    if (CheckIfTreeViewItemContainsSystem(childItem as SystemTreeViewItem, systemName))
                        return true;
                }
            }
            else
            {
                var itemName = item.GetSystemName();
                itemName = Regex.Replace(itemName, @"[(].*", string.Empty);
                itemName = Regex.Replace(itemName, @"\s+", string.Empty, RegexOptions.IgnoreCase).Trim();

                if (itemName.IndexOf(systemName, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }
    }
}
