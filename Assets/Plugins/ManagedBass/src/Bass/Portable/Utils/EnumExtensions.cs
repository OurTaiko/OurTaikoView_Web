using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace ManagedBass
{
    public static class EnumExtensions
    {
        public static bool Has<T>(this T flags, T flag) where T : unmanaged, Enum
        {
            var targetBit = Unsafe.As<T, ulong>(ref flag);
            var sourceBit = Unsafe.As<T, ulong>(ref flags);
            return (sourceBit & targetBit) != 0;
        }
    }
}
