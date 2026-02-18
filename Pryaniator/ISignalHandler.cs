namespace Pryaniator;

public interface ISignalHandler<in TSignal> where TSignal : Signal
{
    Task Handle(TSignal signal, CancellationToken cancellationToken);
}

public interface ISignalHandler<in TSignal, TResult> where TSignal : Signal
{
    Task<TResult> Handle(TSignal signal, CancellationToken cancellationToken);
}
