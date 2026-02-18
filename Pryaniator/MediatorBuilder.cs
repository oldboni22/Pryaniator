using System.Collections.Concurrent;
using System.Collections.Frozen;
using System.Linq.Expressions;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Pryaniator;

internal static class MediatorBuilder
{
    private static readonly Type NullReturnDefinition = typeof(ISignalHandler<>).GetGenericTypeDefinition();
    
    private static readonly Type ValueReturnDefinition = typeof(ISignalHandler<,>).GetGenericTypeDefinition();
    
    public static FrozenDictionary<Type, IEnumerable<Func<IServiceProvider, Signal, CancellationToken, Task<object?>>>> CreateDictionary(params Assembly[] assemblies)
    {
        var handlers = GetValidHandlerTypes(assemblies).ToList();

        var registry = handlers
            .SelectMany(hType => hType.GetInterfaces(), (hType, iType) => new { hType, iType })
            .Where(x => x.iType.IsGenericType)
            .Select(x => new
            {
                x.hType,
                x.iType,
                Def = x.iType.GetGenericTypeDefinition(),
                Args = x.iType.GetGenericArguments()
            })
            .Where(x => x.Def == ValueReturnDefinition || x.Def == NullReturnDefinition)
            .Select(x => new
            {
                SignalType = x.Args[0],
                ResultType = x.Args.Length > 1 ? x.Args[1] : null,
                HandlerType = x.hType,
                InterfaceType = x.iType
            })
            .GroupBy(x => x.SignalType);
        
        var result = new ConcurrentDictionary<Type, IEnumerable<Func<IServiceProvider, Signal, CancellationToken, Task<object?>>>>();
        
        Parallel.ForEach(registry, group =>
        {
            var invokers = new ConcurrentBag<Func<IServiceProvider, Signal, CancellationToken, Task<object?>>>();
    
            foreach (var entry in group)
            {
                invokers.Add(GenerateExpressionInvoker(entry.HandlerType, entry.InterfaceType, entry.SignalType, entry.ResultType));
            }
    
            result[group.Key] = invokers;
        });

        return result.ToFrozenDictionary();
    }

    #region Invocation generation

    private static Func<IServiceProvider, Signal, CancellationToken, Task<object?>> GenerateExpressionInvoker(
        Type handlerType, Type handlerInterface, Type signalType, Type? resultType)
    {
        var handlerFactory = ActivatorUtilities.CreateFactory(handlerType, []);
        
        var ctParam = Expression.Parameter(typeof(CancellationToken), "ct");
        var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
        var signalParam = Expression.Parameter(typeof(Signal), "signal");
        
        var factoryConstant =  Expression.Constant(handlerFactory);
        var createHandlerInvoker = Expression.Invoke(factoryConstant, spParam, Expression.Constant(null, typeof(object[])));
        
        var resolvedHandler = Expression.Convert(createHandlerInvoker, handlerType);
        
        var handleMethod = handlerInterface.GetMethod("Handle", [signalType, typeof(CancellationToken)]);
        var convertedSignal = Expression.Convert(signalParam, signalType);
        var callHandle = Expression.Call(resolvedHandler, handleMethod!, convertedSignal, ctParam);

        Expression finalBody;

        if (resultType == null)
        {
            var wrapMethod = typeof(TaskWrappers).GetMethod(nameof(TaskWrappers.WrapNullTask))!;
            finalBody = Expression.Call(wrapMethod, callHandle, resolvedHandler);
        }
        else
        {
            var wrapMethod = typeof(TaskWrappers)
                .GetMethod(nameof(TaskWrappers.WrapValueTask))!
                .MakeGenericMethod(resultType);
            finalBody = Expression.Call(wrapMethod, callHandle, resolvedHandler);
        }
        
        return Expression.Lambda<Func<IServiceProvider, Signal, CancellationToken, Task<object?>>>(
            finalBody, spParam, signalParam, ctParam).Compile();
    }
    
    #endregion
    
    #region Assemlies scan

    private static IEnumerable<Type> GetValidHandlerTypes(Assembly[] assemblies)
    {
        return assemblies
            .AsParallel()
            .SelectMany(a => a
                .GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false })
                .Where(IsValidHandler));
    }

    private static bool IsValidHandler(Type type)
    {
        return type
            .GetInterfaces()
            .Any(i =>
            {
                if (!i.IsGenericType)
                {
                    return false;
                }
                
                var genericTypeDefinition = i.GetGenericTypeDefinition();
                
                return 
                    genericTypeDefinition == ValueReturnDefinition || genericTypeDefinition == NullReturnDefinition;
            });
    }
    
    #endregion
}

file static class ExceptionMessages
{
    public static string GetMultipleHandlersMessage(Type handlerType) => $"Multiple handlers detected on type {handlerType}";
}

file static class TaskWrappers
{
    public static async Task<object?> WrapNullTask(Task task, object handler)
    {
        try
        {
            await task;
            return null;
        }
        finally
        {
            if (handler is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
    
    public static async Task<object?> WrapValueTask<T>(Task<T> task, object handler)
    {
        try
        {
            return await task;
        }
        finally
        {
            if (handler is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }
}