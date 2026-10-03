using Dignus.Log;

namespace Eidolon.Core.Application
{
    public class StudioWorkQueue : IAsyncDisposable
    {
        private readonly object _gate = new object();
        private readonly Queue<WorkRequest> _requests = new Queue<WorkRequest>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly Task _worker;
        private CancellationTokenSource _currentLifetime;
        private string _currentTitle = string.Empty;
        private bool _accepting = true;

        public event Action Changed;

        public bool IsIdle
        {
            get
            {
                lock (_gate)
                {
                    return _requests.Count == 0 && _currentLifetime == null;
                }
            }
        }

        public string CurrentTitle
        {
            get
            {
                lock (_gate)
                {
                    return _currentTitle;
                }
            }
        }

        public StudioWorkQueue()
        {
            _worker = Task.Run(ProcessAsync);
        }

        public IReadOnlyList<string> GetPendingTitles()
        {
            lock (_gate)
            {
                return _requests.Select(request => request.Title).ToArray();
            }
        }

        public void Enqueue(string title, Func<CancellationToken, Task> execute)
        {
            ArgumentNullException.ThrowIfNull(execute);
            lock (_gate)
            {
                if (_accepting == false)
                {
                    throw new ObjectDisposedException(nameof(StudioWorkQueue));
                }
                _requests.Enqueue(new WorkRequest(title, execute));
                _signal.Release();
            }
            Changed?.Invoke();
        }

        public Task CancelCurrentAsync()
        {
            lock (_gate)
            {
                if (_currentLifetime != null)
                {
                    return _currentLifetime.CancelAsync();
                }
                return Task.CompletedTask;
            }
        }

        public void ClearPending()
        {
            lock (_gate)
            {
                _requests.Clear();
            }
            Changed?.Invoke();
        }

        private async Task ProcessAsync()
        {
            try
            {
                while (true)
                {
                    await _signal.WaitAsync(_lifetime.Token).ConfigureAwait(false);
                    WorkRequest request;
                    CancellationTokenSource currentLifetime;
                    lock (_gate)
                    {
                        if (_requests.Count == 0)
                        {
                            continue;
                        }
                        request = _requests.Dequeue();
                        currentLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                        _currentLifetime = currentLifetime;
                        _currentTitle = request.Title;
                    }
                    Changed?.Invoke();
                    try
                    {
                        currentLifetime.Token.ThrowIfCancellationRequested();
                        await request.Execute(currentLifetime.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (currentLifetime.IsCancellationRequested == true)
                    {
                    }
                    catch (Exception error)
                    {
                        LogHelper.Error(error);
                    }
                    finally
                    {
                        lock (_gate)
                        {
                            _currentLifetime = null;
                            _currentTitle = string.Empty;
                            currentLifetime.Dispose();
                        }
                        Changed?.Invoke();
                    }
                }
            }
            catch (OperationCanceledException) when (_lifetime.IsCancellationRequested == true)
            {
            }
        }

        public async ValueTask DisposeAsync()
        {
            lock (_gate)
            {
                if (_accepting == false)
                {
                    return;
                }
                _accepting = false;
                _requests.Clear();
            }
            _lifetime.Cancel();
            await _worker.ConfigureAwait(false);
            _signal.Dispose();
            _lifetime.Dispose();
        }

        private class WorkRequest
        {
            public string Title { get; }
            public Func<CancellationToken, Task> Execute { get; }

            public WorkRequest(string title, Func<CancellationToken, Task> execute)
            {
                Title = title;
                Execute = execute;
            }
        }
    }
}
