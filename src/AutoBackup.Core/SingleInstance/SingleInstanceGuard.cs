namespace AutoBackup.Core.SingleInstance;

public class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    public bool IsFirstInstance { get; }

    public SingleInstanceGuard(string mutexName)
    {
        _mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        IsFirstInstance = createdNew;
    }

    public void Dispose()
    {
        if (IsFirstInstance)
        {
            try { _mutex.ReleaseMutex(); } catch (ApplicationException) { /* already released */ }
        }
        _mutex.Dispose();
    }
}
