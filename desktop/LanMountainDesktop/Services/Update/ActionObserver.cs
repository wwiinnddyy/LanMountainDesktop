namespace LanMountainDesktop.Services.Update;

internal sealed class ActionObserver<T> : IObserver<T>
{
    private readonly Action<T> _onNext;

    public ActionObserver(Action<T> onNext)
    {
        _onNext = onNext;
    }

    public void OnCompleted() { }
    public void OnError(Exception error) { }
    public void OnNext(T value) => _onNext(value);
}
