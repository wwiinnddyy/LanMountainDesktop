using LanMountainDesktop.Shared.Contracts.Update;

namespace LanMountainDesktop.Services.Update;

internal sealed class UpdateProgressSubject : IObservable<InstallProgressReport>, IObserver<InstallProgressReport>
{
    private readonly List<IObserver<InstallProgressReport>> _observers = [];
    private readonly object _gate = new();
    private bool _completed;

    public IDisposable Subscribe(IObserver<InstallProgressReport> observer)
    {
        lock (_gate)
        {
            if (_completed)
            {
                observer.OnCompleted();
                return EmptyDisposable.Instance;
            }

            _observers.Add(observer);
        }

        return new Subscription(this, observer);
    }

    public void OnNext(InstallProgressReport value)
    {
        IObserver<InstallProgressReport>[] snapshot;
        lock (_gate)
        {
            snapshot = _observers.ToArray();
        }

        foreach (var observer in snapshot)
        {
            observer.OnNext(value);
        }
    }

    public void OnError(Exception error)
    {
        IObserver<InstallProgressReport>[] snapshot;
        lock (_gate)
        {
            _completed = true;
            snapshot = _observers.ToArray();
            _observers.Clear();
        }

        foreach (var observer in snapshot)
        {
            observer.OnError(error);
        }
    }

    public void OnCompleted()
    {
        IObserver<InstallProgressReport>[] snapshot;
        lock (_gate)
        {
            _completed = true;
            snapshot = _observers.ToArray();
            _observers.Clear();
        }

        foreach (var observer in snapshot)
        {
            observer.OnCompleted();
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly UpdateProgressSubject _subject;
        private IObserver<InstallProgressReport>? _observer;

        public Subscription(UpdateProgressSubject subject, IObserver<InstallProgressReport> observer)
        {
            _subject = subject;
            _observer = observer;
        }

        public void Dispose()
        {
            if (_observer is null)
            {
                return;
            }

            lock (_subject._gate)
            {
                _subject._observers.Remove(_observer);
            }

            _observer = null;
        }
    }

    private sealed class EmptyDisposable : IDisposable
    {
        public static readonly EmptyDisposable Instance = new();
        public void Dispose() { }
    }
}
