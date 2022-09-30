using System;

partial class Program
{
    static Type _Startup_Type;
    static Type Startup_Type => _Startup_Type;

    [System.Runtime.CompilerServices.ModuleInitializerAttribute]
    internal static void _it_can_define_more_times_but_need_care_the_orderby() { }

    [System.Runtime.CompilerServices.ModuleInitializer()]
    internal static void OnModuleInitialize()
    {
        _Startup_Type = (
        
        typeof(TestNs.CSharp.MethodInterceptor.Program_test_DispatchProxy2)

        //typeof(Program)
        );
    }
    
}