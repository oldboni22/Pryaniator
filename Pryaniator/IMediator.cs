namespace Pryaniator;

public interface IMediator
{
    Task<object?> SendAsync<TSignal>(TSignal signal) where TSignal : Signal;
}
