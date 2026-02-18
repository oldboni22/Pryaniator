namespace Pryaniator;

public interface IMediator
{
    Task<IEnumerable<object?>> SendAsync<TSignal>(TSignal signal, CancellationToken cancellationToken = default)
        where TSignal : Signal;
}
