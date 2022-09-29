// rg at 2022/9/30 2:00:36

using Common;
using Microsoft.Extensions.DependencyInjection;
using Common.DI;
using System;
using System.Collections.Generic; 

namespace TestNs.DotNet.MethodInterceptor
{ 
	partial class Program_test_DispatchProxy2
	{
		partial class Fx1_MethodInterceptor : rg.__IDInjectInit__
		{ 
		    public virtual void OnCtor(System.IServiceProvider services) => (this as rg.__IDInjectInit__)?.__DInjectInit__(services);
		
		    void rg.__IDInjectInit__.__DInjectInit__(System.IServiceProvider di) { __DInjectInit__(di); } 
		
		    protected virtual void __DInjectInit__(System.IServiceProvider di) 
		    { 
		        this.log = di.GetService<Microsoft.Extensions.Logging.ILogger>(); 
		    }
		
		    static string __DInjectInit____rg_di_key_error__(System.Exception ex) => throw ex;
		} 
		
	}
}
