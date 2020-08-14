namespace Unity.Entities.Editor.Tests
{
    [DisableAutoCreation]
    [UpdateBefore(typeof(SystemScheduleTestSystem2))]
    class SystemScheduleTestSystem1 : ComponentSystem
    {
        EntityQuery m_Group;

        protected override void OnUpdate()
        {
        }

        protected override void OnCreate()
        {
            m_Group = GetEntityQuery(typeof(SystemScheduleTestData1), ComponentType.ReadOnly<SystemScheduleTestData2>());
        }

        protected override void OnDestroy()
        {
        }
    }

    [DisableAutoCreation]
    [UpdateAfter(typeof(SystemScheduleTestSystem1))]
    class SystemScheduleTestSystem2 : ComponentSystem
    {
        protected override void OnUpdate()
        {
        }
    }
}
