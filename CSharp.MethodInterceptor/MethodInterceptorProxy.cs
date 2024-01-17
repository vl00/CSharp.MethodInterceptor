using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace CSharp.MethodInterceptor;

public interface IMethodInvocation
{
    object Instance { get; }
    MethodInfo Method { get; }
    object[] Arguments { get; }

    IReadOnlyDictionary<string, object> ArgumentsDictionary { get; }

    object Result { get; set; }

    Task NextAsync();
}

public interface IMethodInterceptor
{
    Task InvokeAsync(IMethodInvocation ctx);
}

public class MethodInterceptorProxy : DispatchProxy
{
    public object Instance;

    public IEnumerable<object> Intercepters;

    public static MethodInterceptorProxy NewProxy<T>() => Create<T, MethodInterceptorProxy>() as MethodInterceptorProxy;

    public static MethodInterceptorProxy NewProxy(Type type)
    {
#if NET8_0_OR_GREATER
        return DispatchProxy.Create(type, typeof(MethodInterceptorProxy)) as MethodInterceptorProxy;
#else
        var mi = typeof(DispatchProxy).GetMethod(nameof(Create), 2, BindingFlags.Public | BindingFlags.Static, null, Type.EmptyTypes, null);
        return mi.MakeGenericMethod(type, typeof(MethodInterceptorProxy)).Invoke(null, null) as MethodInterceptorProxy;
#endif
    }

    public static T New<T>(T obj, IEnumerable<object> intercepters = null)
    {
        var p = Create<T, MethodInterceptorProxy>();
        var proxy = (p as MethodInterceptorProxy)!;
        proxy.Instance = obj;
        proxy.Intercepters = intercepters;
        return p;
    }

    protected sealed override object Invoke(MethodInfo targetMethod, object[] args)
    {
        var ctx = MethodInvocation.Create(Instance, targetMethod, args);        
        var task = PrivateInvokeAsync(ctx);
        return ctx.ReturnSync(task);
    }

    protected internal Task<object> InvokeAsync(MethodInfo targetMethod, object[] args)
    {
        var ctx = MethodInvocation.Create(Instance, targetMethod, args);
        return PrivateInvokeAsync(ctx);
    }

    private async Task<object> PrivateInvokeAsync(MethodInvocation ctx)
    {
        ctx._intercepters = Intercepters?.GetEnumerator();
        try
        {
            await ctx.NextAsync();
            return ctx.Result;
        }
        finally
        {
            if (ctx._intercepters != null)
            {
                ctx._intercepters.Dispose();
                ctx._intercepters = null;
            }
        }
    }

    enum RetTyEnum
    {
        Normal = 0,         // void | T | ref T
        Task = 1,           // Task
        Task_T = 2,         // Task<T>
        ValueTask = 3,      // ValueTask
        ValueTask_T = 4,    // ValueTask<T>
    }

    class MethodInvocation : IMethodInvocation
    {
        Lazy<ReadOnlyArgumentsDictionary> _argsDictionary;
        RetTyEnum _ret;
        internal IEnumerator<object> _intercepters;

        public object Instance { get; set; }
        public MethodInfo Method { get; set; }
        public object[] Arguments { get; set; }
        public IReadOnlyDictionary<string, object> ArgumentsDictionary => _argsDictionary.Value;
        public object Result { get; set; }

        internal static MethodInvocation Create(object instance, MethodInfo method, object[] args)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            EnsureRetTyEnum(method, out var _ret);

            var ctx = _ret == RetTyEnum.Task_T || _ret == RetTyEnum.ValueTask_T
                ? Activator.CreateInstance(typeof(MethodInvocation<>).MakeGenericType(method.ReturnType.GetGenericArguments()[0])) as MethodInvocation
                : new MethodInvocation();

            ctx.Instance = instance;
            ctx.Method = method;
            ctx.Arguments = args;
            ctx._ret = _ret;
            ctx._argsDictionary = new(() => new(ctx.Method, ctx.Arguments));
            return ctx;
        }

        internal static void EnsureRetTyEnum(MethodInfo method, out RetTyEnum _ret)
        {
            if (method?.ReturnParameter == null || method.ReturnParameter.ParameterType == typeof(void)
                || method.ReturnParameter.IsOut || method.ReturnParameter.IsIn || method.ReturnType.IsByRef)
            {
                //* Method.ReturnType.IsByRef 表示方法为` ref T MethodName(....); ` T可以是引用类型
                //
                //* System.Reflection.DispatchProxy.Create<,>()方法 不能用于有ref返回类型的接口, 否则报错如下:                
                // System.ArgumentException: Cannot get TypeToken for a ByRef type.
                //   at System.Reflection.Emit.ModuleBuilder.GetTypeTokenWorkerNoLock(Type type, Boolean getGenericDefinition)
                //   at System.Reflection.Emit.ModuleBuilder.GetTypeTokenInternal(Type type, Boolean getGenericDefinition)
                //   at System.Reflection.Emit.ILGenerator.Emit(OpCode opcode, Type cls)
                //   at System.Reflection.DispatchProxyGenerator.ProxyBuilder.Convert(ILGenerator il, Type source, Type target, Boolean isAddress)
                //   at System.Reflection.DispatchProxyGenerator.ProxyBuilder.AddMethodImpl(MethodInfo mi, Int32 methodInfoIndex)
                //   at System.Reflection.DispatchProxyGenerator.ProxyBuilder.AddInterfaceImpl(Type iface)
                //   at System.Reflection.DispatchProxyGenerator.GenerateProxyType(Type baseType, Type interfaceType)
                //   at System.Reflection.DispatchProxyGenerator.GetProxyType(Type baseType, Type interfaceType)
                //   at System.Reflection.DispatchProxyGenerator.CreateProxyInstance(Type baseType, Type interfaceType)
                //   at System.Reflection.DispatchProxy.Create[T, TProxy]()

                _ret = RetTyEnum.Normal;
                return;
            }
            if (method.ReturnParameter.ParameterType == typeof(Task))
            {
                _ret = RetTyEnum.Task;
                return;
            }
            if (method.ReturnParameter.ParameterType == typeof(ValueTask))
            {
                _ret = RetTyEnum.ValueTask;
                return;
            }
            if (method.ReturnParameter.ParameterType.IsGenericType)
            {
                var tdef = method.ReturnParameter.ParameterType.GetGenericTypeDefinition();
                if (tdef == typeof(Task<>))
                {
                    _ret = RetTyEnum.Task_T;
                    return;
                }
                if (tdef == typeof(ValueTask<>))
                {
                    _ret = RetTyEnum.ValueTask_T;
                    return;
                }
            }
            //
            // 其它 async/await type ?
            //
            _ret = RetTyEnum.Normal;
        }

        public Task NextAsync()
        {
            while (_intercepters?.MoveNext() == true)
            {
                switch (_intercepters.Current)
                {
                    case null:
                    default:
                        continue;

                    case IMethodInterceptor m:
                        return m.InvokeAsync(this) ?? Task.CompletedTask;

                    case Func<IMethodInvocation, Task> f:
                        return f(this) ?? Task.CompletedTask;
                }                
            }

            var rr = Method.Invoke(Instance, Arguments);     
            if (rr == null) return Task.CompletedTask;
            switch (_ret)
            {                
                case RetTyEnum.Task:
                    {
#if NET6_0_OR_GREATER || NETCOREAPP3_1_OR_GREATER
                        return Unsafe.As<object, Task>(ref rr);
#else
                        return (Task)rr;
#endif
                    }
                case RetTyEnum.Task_T:
                    {                        
                        return SetTaskResult(rr);
                    }
                case RetTyEnum.ValueTask:
                    {
                        //var vt = Unsafe.As<object, ValueTask>(ref rr);
                        //return vt.AsTask(); // 转换不成功,后续使用报错

                        if (rr is ValueTask v && !v.IsCompletedSuccessfully) return v.AsTask();
                        return Task.CompletedTask;
                    }
                case RetTyEnum.ValueTask_T:
                    {
                        return SetValueTaskResult(rr);
                    }

                default:
                case RetTyEnum.Normal:
                    Result = rr;
                    break;
            }
            return Task.CompletedTask;
        }

        internal object ReturnSync(Task<object> task)
        {
            switch (_ret)
            {
                case RetTyEnum.Normal:
                    return task.GetAwaiter().GetResult();

                case RetTyEnum.Task:
                    return task;

                case RetTyEnum.Task_T:
                    return ToTypedTask(task);

                case RetTyEnum.ValueTask:
                    return new ValueTask(task);

                case RetTyEnum.ValueTask_T:
                    return ToTypedValueTask(task);
            }
            throw new NotSupportedException();
        }

        protected virtual Task SetTaskResult(object t) => throw new NotSupportedException();
        protected virtual Task SetValueTaskResult(object t) => throw new NotSupportedException();
        protected virtual object ToTypedTask(Task<object> task) => throw new NotSupportedException();
        protected virtual object ToTypedValueTask(Task<object> task) => throw new NotSupportedException();
    }

    sealed class MethodInvocation<T> : MethodInvocation
    {
        protected override async Task SetTaskResult(object t)
        {            
            if (t is Task<T> task)
            {
                if (task.IsCompletedSuccessfully) this.Result = task.Result;
                else
                {
                    var r = await task.ConfigureAwait(false);
                    this.Result = r;
                }
            }
        }

        protected override async Task SetValueTaskResult(object t)
        {
            if (t is ValueTask<T> vtk2)
            {
                if (vtk2.IsCompletedSuccessfully) this.Result = vtk2.Result;
                else
                {
                    var r = await vtk2.ConfigureAwait(false);
                    this.Result = r;
                }
            }
        }

        protected override object ToTypedTask(Task<object> task) => To_TypedTask(task);

        protected override object ToTypedValueTask(Task<object> task) => To_TypedValueTask(task);

        static Task<T> To_TypedTask(Task<object> task)
        {
            if (typeof(T) == typeof(object))
                return task as Task<T>;

            return task.Status switch
            {
                TaskStatus.RanToCompletion => Task.FromResult((T)task.GetAwaiter().GetResult()),
                TaskStatus.Canceled => Task.FromCanceled<T>(new(true)),
                _ => ConvertAsync(task),
            };
        }

        static ValueTask<T> To_TypedValueTask(Task<object> task)
        {
            if (typeof(T) == typeof(object))
                return new(task as Task<T>);

            return task.Status switch
            {
                TaskStatus.RanToCompletion => new((T)task.GetAwaiter().GetResult()),
#if NET6_0_OR_GREATER
                TaskStatus.Canceled => ValueTask.FromCanceled<T>(new(true)),
#else
                TaskStatus.Canceled => new(Task.FromCanceled<T>(new(true))),
#endif
                _ => new(ConvertAsync(task)),
            };
        }

        static async Task<T> ConvertAsync(Task<object> asyncTask)
        {
            var result = await asyncTask.ConfigureAwait(false);
            if (result == null)
            {
                var type = typeof(T);
                var isNullableType = !type.IsValueType || type.IsConstructedGenericType && type.GetGenericTypeDefinition() == typeof(Nullable<>);
                if (!isNullableType)
                {
                    throw new InvalidCastException($"Expected result of type {type} but encountered a null value. This may be caused by a grain call filter swallowing an exception.");
                }
                return default;
            }
            return (T)result;
        }
    }
}

public sealed class ReadOnlyArgumentsDictionary : IReadOnlyDictionary<string, object>, IEqualityComparer<string>
{
    readonly Dictionary<string, int> _argsIdxs;
    readonly object[] Arguments;

    public ReadOnlyArgumentsDictionary(MethodInfo method, object[] arguments)
    {
        Arguments = arguments;
        _argsIdxs = new Dictionary<string, int>(this);

        var parameters = method.GetParameters();
        for (int i = 0, len = parameters.Length; i < len; i++)
        {
            // 尝试使用参数名忽略大小写
            if (_argsIdxs.TryAdd(parameters[i].Name, i)) continue;
            _argsIdxs[parameters[i].Name] = i;
        }
    }

    bool IEqualityComparer<string>.Equals(string x, string y) => string.Equals(x, y, StringComparison.OrdinalIgnoreCase);
    int IEqualityComparer<string>.GetHashCode(string obj) => obj.ToLower().GetHashCode();

    IEnumerable<string> IReadOnlyDictionary<string, object>.Keys => _argsIdxs?.Keys ?? Enumerable.Empty<string>();
    IEnumerable<object> IReadOnlyDictionary<string, object>.Values => Arguments;

    public int Count => Arguments?.Length ?? 0;

    public object this[string key] => TryGetValue(key, out var v) ? v : null;

    public bool ContainsKey(string key) => _argsIdxs?.ContainsKey(key) ?? false;

    public bool TryGetValue(string key, out object value)
    {
        value = null;
        if (_argsIdxs?.TryGetValue(key, out var i) != true) return false;
        value = Arguments[i];
        return true;
    }

    public Enumerator GetEnumerator() => new(this);
    
    IEnumerator<KeyValuePair<string, object>> IEnumerable<KeyValuePair<string, object>>.GetEnumerator() => GetEnumerator();
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public struct Enumerator : IEnumerator<KeyValuePair<string, object>>, IEnumerator
    {
        readonly ReadOnlyArgumentsDictionary _this;
        Dictionary<string, int>.Enumerator _idxs;

        internal Enumerator(ReadOnlyArgumentsDictionary _this)
        {
            this._this = _this;
            _idxs = _this._argsIdxs?.GetEnumerator() ?? default;
        }

        public KeyValuePair<string, object> Current
        {
            get
            {
                var kv = _idxs.Current;
                return KeyValuePair.Create(kv.Key, _this.Arguments[kv.Value]);
            }
        }

        object IEnumerator.Current => Current;

        public bool MoveNext() => _idxs.MoveNext();

        void IEnumerator.Reset()
        {
            _idxs.Dispose();
            _idxs = _this._argsIdxs.GetEnumerator();
        }

        public void Dispose() { }
    }
}
