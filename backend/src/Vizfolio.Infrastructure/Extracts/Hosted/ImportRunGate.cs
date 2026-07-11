namespace Vizfolio.Infrastructure.Extracts.Hosted;

public sealed class ImportRunGate : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);

    public bool TryAcquire(out IDisposable? handle)
    {
        if (_semaphore.Wait(0))
        {
            handle = new Releaser(_semaphore);
            return true;
        }

        handle = null;
        return false;
    }

    public async Task<IDisposable> AcquireAsync(CancellationToken cancellationToken)
    {
        await _semaphore.WaitAsync(cancellationToken);
        return new Releaser(_semaphore);
    }

    public void Dispose() => _semaphore.Dispose();

    private sealed class Releaser : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private int _released;

        public Releaser(SemaphoreSlim semaphore) => _semaphore = semaphore;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
                _semaphore.Release();
        }
    }
}
