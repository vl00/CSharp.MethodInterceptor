using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

sealed class ProgramCtx : Dictionary<string, object> 
{
    public new object this[string key]
    {
        get => TryGetValue(key, out var value) ? value : null;
        set => base[key] = value;
    }
}

sealed partial class Program
{
    static async Task Main(string[] args)
    {
        var type = Startup_Type;
        var runContinue = Environment.GetEnvironmentVariable("RUN_CONTINUE")?.Trim('"')?.Trim()?.ToLower();
        runContinue = string.IsNullOrEmpty(runContinue) ? "false" : runContinue;

        var dctx = new ProgramCtx()
        {
            ["Startup_Type"] = Startup_Type,
            ["RUN_CONTINUE"] = runContinue,
            ["cwd"] = Directory.GetCurrentDirectory(),
            ["appsettings.json"] = Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json"),
        };
        do
        {
            var med = type.GetMethod("OnPreInit", BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            if (med == null) break;
            if (med.Invoke(null, new object[] { args, dctx }) is Task tsk) await tsk;
            runContinue = dctx["RUN_CONTINUE"]?.ToString();
            runContinue = string.IsNullOrEmpty(runContinue) ? "false" : runContinue;
            type = dctx["Startup_Type"] is Type ty0 ? ty0 : type;
        }
        while (false);

        Console.WriteLine($"cwd='{dctx["cwd"]}'");

        using var host = Host.CreateDefaultBuilder(args)
            .ConfigureAppConfiguration((ctx, builder) =>
            {
                var cbuilder = new ConfigurationBuilder()
                    .SetBasePath($"{dctx["cwd"]}");

                if (File.Exists($"{dctx["appsettings.json"]}"))
                    cbuilder.AddJsonFile($"{dctx["appsettings.json"]}");

                ctx.Configuration = cbuilder.Build();
            })
            .ConfigureLogging((ctx, builder) =>
            {
                builder.AddConsole(options =>
                {
                    options.TimestampFormat = "[MM-dd HH:mm:ss.ffff] ";
                });
            })
            .ConfigureServices((ctx, services) =>
            {
                services.AddSingleton(dctx);
                services.AddTransient(sp => sp.GetRequiredService<ILoggerFactory>().CreateLogger(type));

                var med = type.GetMethod("ConfigureServices", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
                if (med == null) return;
                var ops = SetValuesToMethodParametersFromDI(med, null, 
                    (typeof(IServiceCollection), services), 
                    (typeof(IConfiguration), ctx.Configuration));
                med.Invoke(null, ops);
            })
            .UseConsoleLifetime()
            .Build();

        var sp = host.Services.CreateScope().ServiceProvider;

        Func<Task> task = null;
        do
        {
            var med = type.GetMethod("OnRunAsync", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);
            if (med == null) break;
            if (med.IsStatic)
            {
                var ops = SetValuesToMethodParametersFromDI(med, sp);
                task = () => med.Invoke(null, ops) as Task;
            }
            else
            {
                var obj = Activator.CreateInstance(type);
                object[] ops = null;
                var med2 = type.GetMethod("OnCtor", BindingFlags.Public | BindingFlags.Instance);
                if (med2 != null)                
                {
                    ops = SetValuesToMethodParametersFromDI(med2, sp);
                }
                task = () =>
                {
                    med2?.Invoke(obj, ops);
                    return med.Invoke(obj, null) as Task;
                };
            }
        } while (false);

        Exception _ex = null;
        async Task ftsk()
        {
            if (task == null) return;
            var log = sp.GetService<Microsoft.Extensions.Logging.ILogger>();
            try
            {
                log?.LogDebug($"Program startup-type= '{type.Name}'");
                log?.LogInformation("[---run start-------------------------------------------------]");
                var t = task();
                if (t != null) await t;
            }
            catch (Exception ex)
            {
                log?.LogError(ex, string.Empty);
                _ex = ex;
            }
            finally
            {
                log?.LogInformation("[---run end---------------------------------------------------]");
                switch (runContinue)
                {
                    case "1":
                    case "true":
                    case "True":
                    case "onlyonok" when _ex == null:
                        await host.StopAsync();
                        break;
                }
            }
        }

        var ht = host.RunAsync();
        var rt = ftsk();
        await Task.WhenAll(ht); //host.Services.GetService<IHostApplicationLifetime>()
        await Task.WhenAny(Task.Delay(3000), rt);

        //await host.StopAsync();       
        try { (sp as IDisposable)?.Dispose(); } catch { }

        if (_ex != null)
        {
            switch (runContinue) 
            {
                case "false":
                case "False":
                case "0":
                    return;
                default:
                    break;
            }
            throw _ex;
        }
    }

#nullable enable
    static object[] SetValuesToMethodParametersFromDI(MethodInfo med, IServiceProvider? sp, params (Type Type, object Obj)[] consts)
    {
        var ps = med.GetParameters();
        var ops = new object[ps.Length];
        for (var i = 0; i < ops.Length; i++)
        {
            ops[i] = ps[i].ParameterType == typeof(IServiceProvider) ? sp ?? throw new ArgumentNullException("IServiceProvider is null") :
                consts.FirstOrDefault(_ => _.Type == ps[i].ParameterType).Obj ?? (
                    sp?.GetService(ps[i].ParameterType) ?? throw new ArgumentException($"IServiceProvider can't resolve type '{ps[i].ParameterType.FullName}'")
                );
        }
        return ops;
    }
#nullable disable

    /*// 
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        //...
    }

    public static [Task|void] OnRunAsync(IServiceProvider services, ILogger log, IHostApplicationLifetime appLifetime)
    {
        //log.LogInformation("started");
        //...
    }

    public void OnCtor(IServiceProvider services, ILogger _log, IConfiguration _config, IHostApplicationLifetime appLifetime)
    {
        //...
    }

    internal void DInject_init_{XXX}() 
    {
        //...
    }

    public [Task|void] OnRunAsync()
    {
        //...
    }
    //*/
}
