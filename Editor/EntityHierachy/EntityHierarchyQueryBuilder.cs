using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace Unity.Entities.Editor
{
    class EntityHierarchyQueryBuilder
    {
        static readonly Regex k_Regex = new Regex(@"\b(?<token>[cC]:)\s*(?<componentType>(\w|\d)+)", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture);

        readonly Dictionary<string, Type> m_ComponentTypes;

        public EntityHierarchyQueryBuilder()
        {
            m_ComponentTypes = new Dictionary<string, Type>();
        }

        public void Initialize()
        {
            m_ComponentTypes.Clear();
            foreach (var typeInfo in TypeManager.GetAllTypes())
            {
                if ((typeInfo.Category == TypeManager.TypeCategory.ComponentData || typeInfo.Category == TypeManager.TypeCategory.ISharedComponentData) && typeInfo.Type != null)
                    m_ComponentTypes[typeInfo.Type.Name] = typeInfo.Type;
            }
        }

        public EntityQueryDesc BuildQuery(string input, out string unmatchedInput)
        {
            unmatchedInput = input;

            if (string.IsNullOrEmpty(input))
                return null;

            var matches = k_Regex.Matches(input);
            if (matches.Count == 0)
                return null;

            using (var componentTypes = PooledList<ComponentType>.Make())
            {
                var b = new StringBuilder();
                var pos = 0;
                for (var i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    var matchGroup = match.Groups["componentType"];

                    var length = match.Index - pos;
                    if (length > 0)
                        b.Append(input.Substring(pos, length));

                    pos = match.Index + match.Length;

                    if (m_ComponentTypes.TryGetValue(matchGroup.Value, out var includedType))
                        componentTypes.List.Add(includedType);
                }

                if (input.Length - pos > 0)
                    b.Append(input.Substring(pos));

                unmatchedInput = b.ToString();

                if (componentTypes.List.Count == 0)
                    return null;

                return new EntityQueryDesc { Any = componentTypes.List.ToArray() };
            }
        }
    }
}
