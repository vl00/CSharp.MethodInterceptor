using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace rg
{
    [Conditional("DEBUG")]
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false, Inherited = false)]
    public sealed class DInject : Attribute
    {
        public DInject() { }
        public DInject(string key) { } //object??
    }

    internal interface __IDInjectInit__
    {
        //#use//public {className}(IServiceProvider services) => this.DInjectInit(services);

        //#[med]//void DInject_init_{xxx}(); //method name can be lower

        void __DInjectInit__(IServiceProvider di);
    }
}