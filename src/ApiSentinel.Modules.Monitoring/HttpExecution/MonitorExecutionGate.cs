using System.Collections.Concurrent;

namespace ApiSentinel.Modules.Monitoring.HttpExecution;

internal interface IMonitorExecutionGate
{
    IDisposable? TryEnter(Guid monitorId);
}

internal sealed class MonitorExecutionGate : IMonitorExecutionGate
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> _locks = new();

    public IDisposable? TryEnter(Guid monitorId)
    {
        var semaphore = _locks.GetOrAdd(monitorId, static _ => new SemaphoreSlim(1, 1));
        return semaphore.Wait(0) ? new Releaser(semaphore) : null;
    }

    private sealed class Releaser(SemaphoreSlim semaphore) : IDisposable
    {
        private int _released;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _released, 1) == 0)
            {
                semaphore.Release();
            }
        }
    }
}
