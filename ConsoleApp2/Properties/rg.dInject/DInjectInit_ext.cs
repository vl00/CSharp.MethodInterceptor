using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace Common.DI { }

namespace Common
{
    internal static class DInjectInit_ext
    {
        [DebuggerStepThrough]
        public static void DInjectInit<T>(this T obj, IServiceProvider di) => (obj as rg.__IDInjectInit__)?.__DInjectInit__(di);
    }
}