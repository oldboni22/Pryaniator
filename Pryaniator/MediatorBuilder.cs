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
    
    public static FrozenDictionary<Type, Func<IServiceProvider, Signal, Task<object?>>> CreateDictionary(params Assembly[] assemblies)
    {
        var signals = GetValidSignalTypes(assemblies).ToList();
        var handlers = GetValidHandlerTypes(assemblies).ToList();

        var result = new ConcurrentDictionary<Type, Func<IServiceProvider, Signal, Task<object?>>>();

        Parallel.ForEach(handlers, hType =>
        {
            try
            {
                var handlerInterface = hType
                    .GetInterfaces()
                    .Single(i =>
                    {
                        if (!i.IsGenericType)
                        {
                            return false;
                        }

                        var genericTypeDefinition = i.GetGenericTypeDefinition();
                        return
                            genericTypeDefinition == ValueReturnDefinition ||
                            genericTypeDefinition == NullReturnDefinition;
                    });
                
                var genericArgs = handlerInterface.GetGenericArguments();

                var signalType = genericArgs[0];
                var resultType = genericArgs.Length > 1 ? genericArgs[1] : null;

                result.TryAdd(signalType, GenerateExpressionInvoker(hType, handlerInterface, signalType, resultType));
            }
            catch (InvalidOperationException e)
            {
                throw new Exception(ExceptionMessages.GetMultipleHandlersMessage(hType), e);
            }
        });

        return result.ToFrozenDictionary();
    }

    #region Invocation generation

    private static Func<IServiceProvider, Signal, Task<object?>> GenerateExpressionInvoker(
        Type handlerType, Type handlerInterface, Type signalType, Type? resultType)
    {
        var handlerFactory = ActivatorUtilities.CreateFactory(handlerType, []);
        
        var spParam = Expression.Parameter(typeof(IServiceProvider), "sp");
        var signalParam = Expression.Parameter(typeof(Signal), "signal");
        
        var factoryConstant =  Expression.Constant(handlerFactory);
        var createHandlerInvoker = Expression.Invoke(factoryConstant, spParam, Expression.Constant(null, typeof(object[])));
        
        var resolvedHandler = Expression.Convert(createHandlerInvoker, handlerType);
        
        var handleMethod = handlerInterface.GetMethod("Handle", [signalType]);
        var convertedSignal = Expression.Convert(signalParam, signalType);
        var callHandle = Expression.Call(resolvedHandler, handleMethod!, convertedSignal);

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
        
        return Expression.Lambda<Func<IServiceProvider, Signal, Task<object?>>>(finalBody, spParam, signalParam).Compile();
    }
    
    #endregion
    
    #region Assemlies scan

    private static IEnumerable<Type> GetValidSignalTypes(Assembly[] assemblies)
    {
        return assemblies
            .AsParallel()
            .SelectMany(a => a
                .GetTypes()
                .Where(t => t is { IsAbstract: false, IsInterface: false})
                .Where(t => t.BaseType == typeof(Signal)));
    }

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