using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace Unity.Entities.Editor
{
    class EntityHierarchyQueryBuilder
    {
        static readonly Regex k_Regex = new Regex(@"\b(?<token>[cC]:)\s*(?<componentType>(\S)*)", RegexOptions.Compiled | RegexOptions.Singleline | RegexOptions.ExplicitCapture);

        static readonly TypeCache k_TypeCache = new TypeCache();
        readonly StringBuilder m_UnmatchedInputBuilder;

        public EntityHierarchyQueryBuilder()
        {
            m_UnmatchedInputBuilder = new StringBuilder();
        }

        public void Initialize() => k_TypeCache.Initialize();

        public Result BuildQuery(string input)
        {
            if (string.IsNullOrEmpty(input))
                return Result.ValidBecauseEmpty;

            var matches = k_Regex.Matches(input);
            if (matches.Count == 0)
                return Result.Valid(null, input);

            using (var componentTypes = PooledHashSet<ComponentType>.Make())
            {
                m_UnmatchedInputBuilder.Clear();
                var pos = 0;
                for (var i = 0; i < matches.Count; i++)
                {
                    var match = matches[i];
                    var matchGroup = match.Groups["componentType"];

                    var length = match.Index - pos;
                    if (length > 0)
                        m_UnmatchedInputBuilder.Append(input.Substring(pos, length));

                    pos = match.Index + match.Length;

                    if (matchGroup.Value.Length == 0)
                        continue;

                    var results = k_TypeCache.GetMatchingTypes(matchGroup.Value);
                    var resultFound = false;
                    foreach (var result in results)
                    {
                        resultFound = true;
                        componentTypes.Set.Add(result);
                    }

                    if (!resultFound)
                        return Result.Invalid(matchGroup.Value);
                }

                if (input.Length - pos > 0)
                    m_UnmatchedInputBuilder.Append(input.Substring(pos));

                return Result.Valid(new EntityQueryDesc { Any = componentTypes.Set.ToArray() }, m_UnmatchedInputBuilder.ToString());
            }
        }

        public struct Result
        {
            public bool IsValid;
            public EntityQueryDesc QueryDesc;
            public string ErrorComponentType;
            public string Filter;

            public static readonly Result ValidBecauseEmpty = new Result { IsValid = true, QueryDesc = null, Filter = string.Empty, ErrorComponentType = string.Empty };

            public static Result Invalid(string errorComponentType)
                => new Result { IsValid = false, QueryDesc = null, Filter = string.Empty, ErrorComponentType = errorComponentType };

            public static Result Valid(EntityQueryDesc queryDesc, string filter)
                => new Result { IsValid = true, QueryDesc = queryDesc, Filter = filter, ErrorComponentType = string.Empty };
        }

        public class TypeCache
        {
            readonly SortedSet<IndexedType> m_ComponentTypes = new SortedSet<IndexedType>();
            bool m_IsInitialized;

            public void Initialize()
            {
                if (m_IsInitialized)
                    return;

                m_ComponentTypes.Clear();
                foreach (var typeInfo in TypeManager.GetAllTypes())
                {
                    if (typeInfo.Type == null)
                        continue;

                    m_ComponentTypes.Add(new IndexedType(typeInfo.Type.Name.ToLowerInvariant().GetHashCode(), typeInfo.Type));
                    m_ComponentTypes.Add(new IndexedType(typeInfo.Type.FullName.ToLowerInvariant().GetHashCode(), typeInfo.Type));
                }

                m_IsInitialized = true;
            }

            public struct IndexedType : IEquatable<IndexedType>, IComparable<IndexedType>
            {
                readonly int m_Hash;
                public readonly Type Type;

                public IndexedType(int hash, Type type)
                {
                    m_Hash = hash;
                    Type = type;
                }

                public static implicit operator IndexedType(int i)
                    => new IndexedType(i, null);

                public bool Equals(IndexedType other)
                    => m_Hash == other.m_Hash && Type == other.Type;

                public override bool Equals(object obj)
                    => obj is IndexedType other && Equals(other);

                public override int GetHashCode() => m_Hash;

                public int CompareTo(IndexedType other)
                {
                    var comparison = m_Hash.CompareTo(other.m_Hash);
                    return comparison != 0 ? comparison : string.Compare(Type?.AssemblyQualifiedName ?? string.Empty, other.Type?.AssemblyQualifiedName ?? string.Empty, StringComparison.Ordinal);
                }
            }

            public IEnumerable<Type> GetMatchingTypes(string str)
            {
                var typeHash = str.ToLowerInvariant().GetHashCode();
                var indexedTypes = m_ComponentTypes.GetViewBetween(typeHash, typeHash + 1);
                foreach (var indexedType in indexedTypes)
                {
                    yield return indexedType.Type;
                }
            }
        }
    }
}
