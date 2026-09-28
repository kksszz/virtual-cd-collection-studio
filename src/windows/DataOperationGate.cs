namespace ZipMp3Player;

// UI-thread scopes span awaits, validation and cleanup.
internal sealed class DataOperationGate
{
    internal int ActiveCount { get; private set; }
    internal bool CloseRequested { get; private set; }
    internal event Action? Drained;
    internal IDisposable? Begin()
    {
        if (CloseRequested) return null;
        ActiveCount++;
        return new Scope(this);
    }
    internal bool RequestClose() { CloseRequested = true; return ActiveCount == 0; }
    private sealed class Scope(DataOperationGate owner) : IDisposable
    {
        private bool _disposed;
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            if (--owner.ActiveCount == 0 && owner.CloseRequested) owner.Drained?.Invoke();
        }
    }
}
