# DotNet.MethodInterceptor

How to use? See the sample code below :

```
using ...

namespace TestNs.DotNet.MethodInterceptor;

partial class Program_test_DispatchProxy2
{
    public static void ConfigureServices(IServiceCollection services, IConfiguration config)
    {
        services.AddTransient<ITestYL, TestYL>();

        services.AddTransient<Fx1_MethodInterceptor>();
        services.AddTransient<Fx2_MethodInterceptor>();

        services.TryAddMethodInterceptor<TestYL, Fx1_MethodInterceptor>();
        services.TryAddMethodInterceptor<TestYL, Fx2_MethodInterceptor>();

        services.ResolveServiceTypeAndMethodInterceptors();
    }

    [DInject] static IServiceProvider services;
    [DInject] static ILogger log;
    [DInject] static IConfiguration config;

    partial class Fx1_MethodInterceptor : IMethodInterceptor
    {
        public async Task InvokeAsync(IMethodInvocation ctx)
        {
            log.LogInformation($"before call {ctx.Method.Name}");
            try
            {
                await ctx.NextAsync();
                log.LogInformation("after call {MethodName}, r={Result}", ctx.Method.Name, ctx.Result);
            }
            catch (Exception ex)
            {
                log.LogInformation(ex, "after call {MethodName}, r={Result}", ctx.Method.Name, ctx.Result);
            }
        }
    }

    partial class Fx2_MethodInterceptor : IMethodInterceptor
    {
        public async Task InvokeAsync(IMethodInvocation ctx)
        {
            if (ctx.Method.Name == "F1")
            {
                Debugger.Break();
                await Task.Delay(5000);
            }
            await ctx.NextAsync();
        }
    }

    public async Task OnRunAsync()
    {
        var kk = services.GetService<ITestYL>();
        Debugger.Break();
        kk.F1();
        log.LogDebug("==============================================");
        await kk.F2(233);
        log.LogDebug("==============================================");
        var r3 = await kk.F3(true);
        log.LogDebug("==============================================");
        await kk.F4(444);
        log.LogDebug("==============================================");
        var r5 = await kk.F5("555555");
        log.LogDebug("==============================================");
        var b6 = 46.545;
        var r6 = kk.F6(3.07, ref b6, out var c6);
        log.LogInformation("b6={b6}, c6={c6}", b6, c6);
        log.LogDebug("==============================================");
        var s8 = "s8";
        await kk.F8(ref s8);
        log.LogDebug("==============================================");
        await kk.F9(true);
        log.LogDebug("==============================================");
    }

    interface ITestYL
    {
        void F1();
        double F6(double a, ref double b, out double c);
        Task F2(object o);
        Task<int> F3(bool b);
        ValueTask F4(object o);
        ValueTask<int> F5(string s);

        //ref Task F7(int i); // System.ArgumentException: Cannot get TypeToken for a ByRef type.

        Task F8(ref string s);
        ValueTask F9<T>(T p);
    }
    class TestYL : ITestYL
    {
        public void F1()
        {
            log.LogInformation($"{nameof(TestYL)} med={nameof(F1)} called");
        }
        public double F6(double a, ref double b, out double c)
        {
            c = b;
            b = a;
            return a + c;
        }
        public async Task F2(object o)
        {
            log.LogInformation($"{nameof(TestYL)} med={nameof(F2)} called o={o}");
            await Task.Delay(10000);
            log.LogInformation($"{nameof(TestYL)} med={nameof(F2)} called o={o} delay");
        }
        public async Task<int> F3(bool b)
        {
            log.LogInformation($"{nameof(TestYL)} med={nameof(F3)} called b={b}");
            await Task.Delay(7000);
            log.LogInformation($"{nameof(TestYL)} med={nameof(F3)} called b={b} delay");
            return 1;
        }
        public async ValueTask F4(object o)
        {
            log.LogInformation($"{nameof(TestYL)} med={nameof(F4)} called o={o}");
            await Task.Delay(10000);
            log.LogInformation($"{nameof(TestYL)} med={nameof(F4)} called o={o} delay");
        }
        public async ValueTask<int> F5(string s)
        {
            log.LogInformation($"{nameof(TestYL)} med={nameof(F5)} called s={s}");
            await Task.Delay(7000);
            log.LogInformation($"{nameof(TestYL)} med={nameof(F5)} called s={s} delay");
            return 100;
        }

        public ref Task F7(int i)
        {
            var t = new[] { Task.Delay(1000) };
            return ref t[i];
        }
        public Task F8(ref string s)
        {
            log.LogInformation($"{nameof(TestYL)} med={nameof(F8)} called s={s}");
            return Task.Delay(1000);
        }
        public async ValueTask F9<T>(T p)
        {
            await Task.Delay(9000);
            log.LogInformation($"{nameof(TestYL)} med={nameof(F9)} called p={p} delay");
        }
    }
}
```
