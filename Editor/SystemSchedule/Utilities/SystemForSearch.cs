using System;
using System.Linq;
using Unity.Properties;

namespace Unity.Entities.Editor
{
    class SystemForSearch
    {
        public SystemForSearch(SystemHandle systemHandle)
        {
            SystemHandle = systemHandle;
            var systemType = systemHandle.GetSystemType();
            SystemName = Properties.Editor.TypeUtility.GetTypeDisplayName(systemType).Replace(".", "|");
            m_ComponentNamesInQueryCache = EntityQueryUtility.CollectComponentTypesFromSystemQuery(SystemHandle).ToArray();
        }

        public readonly SystemHandle SystemHandle;
        public readonly string SystemName;
        public IPlayerLoopNode Node;

        string[] m_ComponentNamesInQueryCache;
        public string[] SystemDependencyCache;

        [CreateProperty] public string[] ComponentNamesInQuery => m_ComponentNamesInQueryCache;
        [CreateProperty] public string[] SystemDependency => SystemDependencyCache;
    }
}
