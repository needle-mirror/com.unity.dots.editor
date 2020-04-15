using NUnit.Framework;
using Unity.Serialization.Editor;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace Unity.Entities.Editor
{
    abstract class DOTSEditorWindow : EditorWindow
    {
        const string k_NoWorldString = "No World";

        ToolbarMenu m_WorldSelector;
        ToolbarSearchField m_SearchField;

        SharedStateContainer m_SharedState;
        string SharedStateKey => $"{GetType().Name}.{nameof(SharedStateContainer)}";
        SharedStateContainer SharedState => m_SharedState ?? (m_SharedState = SessionState<SharedStateContainer>.GetOrCreateState(SharedStateKey));

        class SharedStateContainer
        {
            public string SelectedWorldName;
            public string SearchFilter;
        }

        protected World GetCurrentlySelectedWorld()
        {
            if (World.All.Count == 0)
            {
                return null;
            }

            World selectedWorld = null;
            foreach (var world in World.All)
            {
                if (world.Name == SharedState.SelectedWorldName)
                {
                    selectedWorld = world;
                    break;
                }
            }

            if (null == selectedWorld)
            {
                selectedWorld = World.All[0];
            }

            SharedState.SelectedWorldName = selectedWorld.Name;
            return selectedWorld;
        }

        protected ToolbarMenu CreateWorldSelector()
        {
            var currentWorld = GetCurrentlySelectedWorld();
            m_WorldSelector = new ToolbarMenu
            {
                name = "worldMenu",
                variant = ToolbarMenu.Variant.Popup,
                text = currentWorld == null ? k_NoWorldString : currentWorld.Name
            };

            UpdateWorldDropDownMenu();

            return m_WorldSelector;
        }
        
        protected void AddSearchField(VisualElement parent, string ussClass)
        {
            m_SearchField = new ToolbarSearchField();
            m_SearchField.AddToClassList(ussClass);
            m_SearchField.RegisterValueChangedCallback(OnFilterChanged);
            parent.Add(m_SearchField);
        } 
        
        protected ToolbarMenu CreateDropDownSettings(string ussClass)
        {
            var dropdownSettings = new ToolbarMenu()
            {
                name = "dropdownSettings",
                variant = ToolbarMenu.Variant.Popup
            };
            Resources.Templates.CommonResources.AddStyles(dropdownSettings);
            dropdownSettings.AddToClassList(UssClasses.Resources.Common);
            dropdownSettings.AddToClassList(ussClass);

            var arrow = dropdownSettings.Q(className: "unity-toolbar-menu__arrow");
            arrow.style.backgroundImage = null;

            return dropdownSettings;
        }
        
        public void Update()
        {
            m_SearchField.value = string.IsNullOrEmpty(SharedState.SearchFilter) ? string.Empty : SharedState.SearchFilter;

            // Worlds could have been added or removed without this window knowing
            if (m_WorldSelector != null)
            {
                UpdateWorldDropDownMenu();
                var currentWorld = GetCurrentlySelectedWorld();
                m_WorldSelector.text = currentWorld == null ? k_NoWorldString : currentWorld.Name;
            }

            OnUpdate();
        }

        void UpdateWorldDropDownMenu()
        {
            if (m_WorldSelector == null)
                return;

            var menu = m_WorldSelector.menu;
            var menuItemsCount = menu.MenuItems().Count;

            for (var i = 0; i < menuItemsCount; i++)
            {
                menu.RemoveItemAt(0);
            }

            var advancedSettings = UserSettings<AdvancedSettings>.GetOrCreate(Constants.Settings.AdvancedSettings); 
                
            if (World.All.Count > 0)
            {
                AppendWorldMenu(menu, advancedSettings.ShowAdvancedWorlds);
            }
            else
            {
                menu.AppendAction(k_NoWorldString, OnWorldSelected, DropdownMenuAction.AlwaysEnabled);
            }
        }

        void AppendWorldMenu(DropdownMenu menu, bool showAdvancedWorlds)
        {
            var worldCategories = WorldCategoryHelper.Categories;

            foreach (var category in worldCategories)
            {
                if (showAdvancedWorlds)
                {
                    menu.AppendAction(category.Name.ToUpper(), null, DropdownMenuAction.Status.Disabled);
                    AppendWorlds(menu, category);
                    menu.AppendSeparator();
                }
                else if (category.Flag == WorldFlags.Live)
                {
                    AppendWorlds(menu, category);
                    break;
                }
            }
        }

        void AppendWorlds(DropdownMenu menu, WorldCategoryHelper.Category category)
        {
            foreach (var world in category.Worlds)
            {
                menu.AppendAction(world.Name, OnWorldSelected, a => 
                    (SharedState.SelectedWorldName == world.Name) 
                        ? DropdownMenuAction.Status.Checked 
                        : DropdownMenuAction.Status.Normal, world);
            }
        }

        void OnWorldSelected(DropdownMenuAction action)
        {
            var world = action.userData as World;
            if (world != null)
            {
                m_WorldSelector.text = world.Name;
                SharedState.SelectedWorldName = world.Name;
            }
            else
            {
                m_WorldSelector.text = k_NoWorldString;
            }

            OnWorldSelected(world);
        }

        void OnFilterChanged(ChangeEvent<string> evt)
        {
            SharedState.SearchFilter = evt.newValue;
            OnFilterChanged(evt.newValue);
        }

        protected abstract void OnUpdate();
        protected abstract void OnWorldSelected(World world);
        protected abstract void OnFilterChanged(string filter);
    }
}