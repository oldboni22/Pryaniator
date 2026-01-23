namespace Pryaniator;

public interface ISignalHandler<in TSignal> where TSignal : Signal
{
    Task Handle(TSignal signal);
}

public interface ISignalHandler<in TSignal, TResult> where TSignal : Signal
{
    Task<TResult> Handle(TSignal signal);
}
