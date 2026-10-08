using Dignus.Collections;
using Dignus.Log;

namespace Eidolon.Core.Application
{
    public class StudioWorkQueue : IAsyncDisposable
    {
        private const int ClosingFlag = int.MinValue;

        private readonly SynchronizedArrayQueue<WorkRequest> _requests = new SynchronizedArrayQueue<WorkRequest>();
        private readonly SemaphoreSlim _signal = new SemaphoreSlim(0);
        private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();
        private readonly TaskCompletionSource _enqueueDrained = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _disposed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly Task _worker;
        private WorkRequest _currentRequest;
        private int _enqueueState;
        private int _processing;

        public event Action Changed;

        public bool IsIdle
        {
            get
            {
                return _requests.Count == 0 && Volatile.Read(ref _processing) == 0;
            }
        }

        public string CurrentTitle
        {
            get
            {
                WorkRequest request = Volatile.Read(ref _currentRequest);
                if (request == null)
                {
                    return string.Empty;
                }
                return request.Title;
            }
        }

        public StudioWorkQueue()
        {
            _worker = Task.Run(ProcessAsync);
        }

        public IReadOnlyList<string> GetPendingTitles()
        {
            return _requests.Select(request => request.Title).ToArray();
        }

        public void Enqueue(string title, Func<CancellationToken, Task> execute, Action discarded = null)
        {
            ArgumentNullException.ThrowIfNull(execute);
            EnterEnqueue();
            try
            {
                _requests.Add(new WorkRequest(title, execute, discarded));
                _signal.Release();
            }
            finally
            {
                if (Interlocked.Decrement(ref _enqueueState) == ClosingFlag)
                {
                    _enqueueDrained.TrySetResult();
                }
            }
            Changed?.Invoke();
        }

        private void EnterEnqueue()
        {
            while (true)
            {
                int state = Volatile.Read(ref _enqueueState);
                if (state < 0)
                {
                    throw new ObjectDisposedException(nameof(StudioWorkQueue));
                }
                if (Interlocked.CompareExchange(ref _enqueueState, state + 1, state) == state)
                {
                    return;
                }
            }
        }

        public Task CancelCurrentAsync()
        {
            WorkRequest request = Volatile.Read(ref _currentRequest);
            if (request == null)
            {
                return Task.CompletedTask;
            }
            return request.CancelAsync();
        }

        public void ClearPending()
        {
            while (_requests.TryRead(out WorkRequest request) == true)
            {
                request.Discard();
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
                    Volatile.Write(ref _processing, 1);
                    if (_requests.TryRead(out WorkRequest request) == false)
                    {
                        Volatile.Write(ref _processing, 0);
                        Changed?.Invoke();
                        continue;
                    }
                    using CancellationTokenSource currentLifetime = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
                    request.StartCancellation(currentLifetime);
                    Volatile.Write(ref _currentRequest, request);
                    try
                    {
                        Changed?.Invoke();
                        currentLifetime.Token.ThrowIfCancellationRequested();
                        await request.Execute(currentLifetime.Token).ConfigureAwait(false);
                    }
                    catch (OperationCanceledException) when (currentLifetime.IsCancellationRequested == true)
                    {
                        request.Discard();
                    }
                    catch (Exception error)
                    {
                        LogHelper.Error(error);
                    }
                    finally
                    {
                        try
                        {
                            await request.FinishAsync().ConfigureAwait(false);
                        }
                        catch (Exception error)
                        {
                            LogHelper.Error(error);
                        }
                        Volatile.Write(ref _currentRequest, null);
                        Volatile.Write(ref _processing, 0);
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
            int state = Interlocked.Or(ref _enqueueState, ClosingFlag);
            if (state < 0)
            {
                await _disposed.Task.ConfigureAwait(false);
                return;
            }
            try
            {
                if (state > 0)
                {
                    await _enqueueDrained.Task.ConfigureAwait(false);
                }
                ClearPending();
                try
                {
                    await _lifetime.CancelAsync().ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        await _worker.ConfigureAwait(false);
                    }
                    finally
                    {
                        _signal.Dispose();
                        _lifetime.Dispose();
                    }
                }
                _disposed.TrySetResult();
            }
            catch (Exception error)
            {
                _disposed.TrySetException(error);
                throw;
            }
        }

    }
}
