using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unity.Entities.Editor
{
    static class SystemScheduleSearchBuilder
    {
        public struct ParseResult : IEquatable<ParseResult>
        {
            public bool IsEmpty => string.IsNullOrWhiteSpace(Input);
            public string Input;
            public IEnumerable<string> ComponentNames;
            public IEnumerable<string> DependencySystemNames;
            public IEnumerable<Type> DependencySystemTypes;
            public IEnumerable<string> SystemNames;

            public bool Equals(ParseResult other)
            {
               return Input == other.Input;
            }

            public static readonly ParseResult EmptyResult = new ParseResult
            {
                Input = string.Empty,
                ComponentNames = Array.Empty<string>(),
                DependencySystemNames = Array.Empty<string>(),
                DependencySystemTypes = Array.Empty<Type>(),
                SystemNames = Array.Empty<string>()
            };
        }

        public static ParseResult ParseSearchString(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return ParseResult.EmptyResult;

            var componentNameList = new Lazy<List<string>>();
            var dependencySystemNameList = new Lazy<List<string>>();
            var dependencySystemTypeList = new Lazy<List<Type>>();
            var systemNameList = new Lazy<List<string>>();

            // TODO: Once we integrate "SearchElement", this "SplitSearchStringBySpace" will be removed.
            foreach (var singleString in SearchUtility.SplitSearchStringBySpace(input))
            {
                if (singleString.StartsWith(Constants.SystemSchedule.k_ComponentToken, StringComparison.OrdinalIgnoreCase))
                {
                    componentNameList.Value.Add(singleString.Substring(Constants.SystemSchedule.k_ComponentTokenLength));
                }
                else if (singleString.StartsWith(Constants.SystemSchedule.k_SystemDependencyToken, StringComparison.OrdinalIgnoreCase))
                {
                    var singleSystemName = singleString.Substring(Constants.SystemSchedule.k_SystemDependencyTokenLength);
                    dependencySystemNameList.Value.Add(singleSystemName);

                    foreach (var system in PlayerLoopSystemGraph.Current.AllSystems)
                    {
                        var type = system.GetType();
                        if (string.Compare(type.Name, singleSystemName, StringComparison.OrdinalIgnoreCase) != 0)
                            continue;

                        dependencySystemTypeList.Value.Add(type);
                        break;
                    }
                }
                else
                {
                    systemNameList.Value.Add(singleString);
                }
            }

            return new ParseResult
            {
                Input = input,
                ComponentNames = componentNameList.Value,
                DependencySystemNames = dependencySystemNameList.Value,
                DependencySystemTypes = dependencySystemTypeList.Value,
                SystemNames = systemNameList.Value
            };
        }
    }
}
