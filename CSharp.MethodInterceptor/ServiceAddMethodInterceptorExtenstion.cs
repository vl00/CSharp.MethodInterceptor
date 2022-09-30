using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CSharp.MethodInterceptor;

public class ServiceAddMethodInterceptorContext
{
    public IList<Type> Interceptors { get; }

    public Type ServiceType { get; }
    public Type ImplementationType { get; }

    public ServiceAddMethodInterceptorContext(Type serviceType, Type implementationType)
    {
        ServiceType = serviceType;
        ImplementationType = implementationType;
        Interceptors = new List<Type>();
    }

    public bool TryAddInterceptor<T>() where T : IMethodInterceptor
    {
        return TryAddInterceptor(typeof(T));
    }

    public bool TryAddInterceptor(Type type)
    {
        if (type == null) return false;
        if (!typeof(IMethodInterceptor).IsAssignableFrom(type)) return false;
        if (Interceptors.Contains(type)) return false;
        Interceptors.Add(type);
        return true;
    }
}

internal class ServiceAddMethodInterceptorActionList : List<Action<ServiceAddMethodInterceptorContext>> { }

public static class ServiceAddMethodInterceptorExtenstion
{
    public static IServiceCollection AddMethodInterceptor(this IServiceCollection services, Action<ServiceAddMethodInterceptorContext> action)
    {
        if (action == null) throw new ArgumentNullException(nameof(action));
        var ls = services.GetOrSetServiceAddMethodInterceptorActionList();
        ls.Add(action);
        return services;
    }

    public static IServiceCollection TryAddMethodInterceptor<TImplementation, TMethodInterceptor>(this IServiceCollection services)
         where TMethodInterceptor : IMethodInterceptor
    {
        return services.AddMethodInterceptor(ctx =>
        {
            if (ctx.ImplementationType == typeof(TImplementation))
            {
                ctx.TryAddInterceptor<TMethodInterceptor>();
            }
        });
    }

    static ServiceAddMethodInterceptorActionList GetOrSetServiceAddMethodInterceptorActionList(this IServiceCollection services, bool setIfNull = true)
    {
        var ls = services.FirstOrDefault(d => d.ServiceType == typeof(ServiceAddMethodInterceptorActionList))?.ImplementationInstance as ServiceAddMethodInterceptorActionList;
        if (ls == null && setIfNull)
        {
            ls = new ServiceAddMethodInterceptorActionList();
            services.Insert(0, ServiceDescriptor.Singleton(ls));
        }
        return ls;
    }

    public static void ResolveServiceTypeAndMethodInterceptors(this IServiceCollection services)
    {
        for (int i = 0, len = services.Count; i < len; i++)
        {
            var descriptor = services[i];
            if (descriptor.ImplementationType == null) continue;

            var ctx = new ServiceAddMethodInterceptorContext(descriptor.ServiceType, descriptor.ImplementationType);
            var actions = services.GetOrSetServiceAddMethodInterceptorActionList(false);
            if (actions != null)
            {
                foreach (var f in actions)
                    f?.Invoke(ctx);
            }

            if (ctx.Interceptors.Count < 1) continue;

            // resolve
            descriptor = ServiceDescriptor.Describe(descriptor.ServiceType, GetCreateInstanceFunc(descriptor.ServiceType, descriptor.ImplementationType, ctx.Interceptors), descriptor.Lifetime);

            services[i] = descriptor;
        }

        var d = services.FirstOrDefault(d => d.ServiceType == typeof(ServiceAddMethodInterceptorActionList));
        if (d != null) services.Remove(d);
    }

    static Func<IServiceProvider, object> GetCreateInstanceFunc(Type serviceType, Type implementationType, IList<Type> interceptorTypes)
    {
        return (sp) => 
        {
            var proxy = MethodInterceptorProxy.NewProxy(serviceType);
            proxy.Instance = ActivatorUtilities.GetServiceOrCreateInstance(sp, implementationType);
            proxy.Intercepters = interceptorTypes.Select(i => sp.GetService(i));
            return proxy;
        };
    }
}
