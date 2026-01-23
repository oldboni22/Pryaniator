using System.Collections.Frozen;

namespace Pryaniator;

public sealed class Mediator(IServiceProvider sp) : IMediator
{
    internal static FrozenDictionary<Type, Func<IServiceProvider, Signal, Task<object?>>> Handlers 
    { 
        private get;
        set => field ??= value;
    }
    
    public Task<object?> SendAsync<TSignal>(TSignal signal) where TSignal : Signal
    {
        return Handlers[typeof(TSignal)](sp, signal);
    }
}
