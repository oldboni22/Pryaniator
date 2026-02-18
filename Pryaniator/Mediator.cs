using System.Collections.Frozen;

namespace Pryaniator;


public sealed class Mediator(
    IServiceProvider sp, 
    FrozenDictionary<Type, IEnumerable<Func<IServiceProvider, Signal, CancellationToken, Task<object?>>>> handlers) : IMediator
{
    public async Task<IEnumerable<object?>> SendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken = default) 
        where TSignal : Signal
    {
        var signalHandlers = handlers[typeof(TSignal)];
        
        var results = new List<object?>();

        foreach (var handler in signalHandlers)
        {
            var handlerResult = await handler(sp, signal, cancellationToken);
                
            results.Add(handlerResult);
        }
        
        return results;
    }
}
