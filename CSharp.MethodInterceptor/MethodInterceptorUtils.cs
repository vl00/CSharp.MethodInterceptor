using System.Threading.Tasks;

namespace CSharp.MethodInterceptor;

public static partial class MethodInterceptorUtils
{
    public static T UnProxy<T>(this T obj)
    {
        return obj is MethodInterceptorProxy p ? (T)p.Instance : obj;
    }

    public static async Task<T> NextAsync<T>(this IMethodInvocation ctx)
    {
        await ctx.NextAsync().ConfigureAwait(false);
        return (T)ctx.Result;
    }

    public static object NextSync(this IMethodInvocation ctx)
    {
        ctx.NextAsync().GetAwaiter().GetResult();
        return ctx.Result;
    }

    public static T NextSync<T>(this IMethodInvocation ctx)
    {
        ctx.NextAsync().GetAwaiter().GetResult();
        return (T)ctx.Result;
    }

    public static ReadOnlyArgumentsDictionary GetArgumentsDictionary(this IMethodInvocation ctx)
    {
        return ctx.ArgumentsDictionary as ReadOnlyArgumentsDictionary;
    }
}
