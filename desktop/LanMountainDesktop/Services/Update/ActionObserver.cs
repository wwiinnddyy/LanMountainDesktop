namespace LanMountainDesktop.Services.Update;

internal static class ObservableHelper<T>
{
    private sealed class EmptyObservable : IObservable<T>
    {
        public IDisposable Subscribe(IObserver<T> observer) => EmptyDisposable.Instance;
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }

    public static readonly IObservable<T> Empty = new EmptyObservable();
}

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
