using UnityEngine;
using System;

namespace Unity.Entities.Editor
{
    unsafe struct SystemHandle : IEquatable<SystemHandle>
    {
        public struct UnmanagedData
        {
            public World World;
            public SystemHandleUntyped Handle;
        }

        public readonly ComponentSystemBase Managed;
        public readonly UnmanagedData Unmanaged;

        public SystemHandle(ComponentSystemBase b)
        {
            Managed = b;
            Unmanaged = default;
        }

        public SystemHandle(SystemHandleUntyped h, World w)
        {
            Managed = null;
            Unmanaged.Handle = h;
            Unmanaged.World = w;
        }

        public SystemState* StatePointer
        {
            get
            {
                if (Managed != null)
                    return Managed.m_StatePtr;
                if (Unmanaged.World != null && Unmanaged.World.IsCreated)
                    return Unmanaged.World.Unmanaged.ResolveSystemState(Unmanaged.Handle);

                return null;
            }
        }

        public World World
        {
            get
            {
                var ptr = StatePointer;
                if (ptr != null)
                    return ptr->World;
                return null;
            }
        }

        public Type GetSystemType()
        {
            if (!Valid) return null;

            return Managed != null ? Managed.GetType() : SystemBaseRegistry.GetStructType(StatePointer->UnmanagedMetaIndex);
        }

        public bool Valid => Managed != null || Unmanaged.World != null;

        public bool Equals(SystemHandle other)
        {
            return ReferenceEquals(Managed, other.Managed) && Unmanaged.Handle == other.Unmanaged.Handle;
        }

        public override int GetHashCode()
        {
            return Managed != null ? Managed.GetHashCode() : Unmanaged.GetHashCode();
        }

        public override bool Equals(object obj)
        {
            if (obj is SystemHandle sel)
            {
                return Equals(sel);
            }

            return false;
        }

        public static bool operator ==(SystemHandle lhs, SystemHandle rhs)
        {
            return lhs.Equals(rhs);
        }

        public static bool operator !=(SystemHandle lhs, SystemHandle rhs)
        {
            return !lhs.Equals(rhs);
        }

        public static implicit operator SystemHandle(ComponentSystemBase arg) => new SystemHandle(arg);

        public override string ToString()
        {
            return GetSystemType().ToString();
        }
    }
}

