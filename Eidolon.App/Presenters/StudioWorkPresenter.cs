using Avalonia.Threading;
using Dignus.DependencyInjection.Attributes;
using Dignus.Log;
using Eidolon.App.Localization;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;

namespace Eidolon.App.Presenters
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class StudioWorkPresenter : IAsyncDisposable
    {
        private readonly StudioWorkQueue _queue;
        private readonly StringHelper _strings;
        private CancellationTokenSource _workLifetime;
        private IProgress<WorkProgress> _progress;
        private bool _closing;
        private CancellationTokenSource _scheduledDelay;
        private Func<CancellationToken, Task> _scheduledAction;
        private string _scheduledTitle;
        private bool _scheduledReady;
        private bool _scheduledExecuting;

        public bool HasScheduledWork
        {
            get
            {
                return _scheduledAction != null || _scheduledExecuting == true;
            }
        }

        public event Action Started;
        public event Action<WorkProgress> ProgressChanged;
        public event Action<StudioWorkState, Exception> Finished;
        public event Action QueueChanged;

        public bool IsIdle
        {
            get
            {
                return _queue.IsIdle;
            }
        }

        public string CurrentTitle
        {
            get
            {
                return _queue.CurrentTitle;
            }
        }

        public IProgress<WorkProgress> Progress
        {
            get
            {
                return _progress;
            }
        }

        public StudioWorkPresenter(StudioWorkQueue queue, StringHelper strings)
        {
            _queue = queue;
            _strings = strings;
            _queue.Changed += OnQueueChanged;
        }

        public IReadOnlyList<string> GetPendingTitles()
        {
            return _queue.GetPendingTitles();
        }

        public void Enqueue(string title, Func<CancellationToken, Task> action)
        {
            if (_closing == true)
            {
                throw new ObjectDisposedException(nameof(StudioWorkPresenter));
            }
            _queue.Enqueue(title, token => ExecuteQueuedAsync(action, token));
            QueueChanged?.Invoke();
            LogHelper.Info(_strings.GetString("EidolonText247") + " " + title);
        }

        public Task CancelCurrentAsync()
        {
            return _queue.CancelCurrentAsync();
        }

        public void ScheduleWhenIdle(string title, Func<CancellationToken, Task> action)
        {
            CancelScheduled();
            if (_closing == true)
            {
                return;
            }
            _scheduledTitle = title;
            _scheduledAction = action;
            _scheduledDelay = new CancellationTokenSource();
            _ = PrepareScheduledAsync(_scheduledDelay.Token);
            QueueChanged?.Invoke();
        }

        public void CancelScheduled()
        {
            _scheduledDelay?.Cancel();
            _scheduledDelay?.Dispose();
            _scheduledDelay = null;
            _scheduledAction = null;
            _scheduledTitle = null;
            _scheduledReady = false;
            QueueChanged?.Invoke();
        }

        private async Task PrepareScheduledAsync(CancellationToken token)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(600), token);
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    if (token.IsCancellationRequested == true || _closing == true)
                    {
                        return;
                    }
                    _scheduledReady = true;
                    EnqueueScheduled();
                });
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested == true)
            {
            }
        }

        private void EnqueueScheduled()
        {
            if (_scheduledReady == false || _queue.IsIdle == false || _closing == true)
            {
                return;
            }
            Func<CancellationToken, Task> action = _scheduledAction;
            string title = _scheduledTitle;
            _scheduledDelay?.Dispose();
            _scheduledDelay = null;
            _scheduledAction = null;
            _scheduledTitle = null;
            _scheduledReady = false;
            _scheduledExecuting = true;
            Enqueue(title, token => ExecuteScheduledAsync(action, token));
        }

        private async Task ExecuteScheduledAsync(Func<CancellationToken, Task> action, CancellationToken token)
        {
            try
            {
                await action(token);
            }
            finally
            {
                _scheduledExecuting = false;
                QueueChanged?.Invoke();
            }
        }

        public void ClearPending()
        {
            _queue.ClearPending();
            QueueChanged?.Invoke();
        }

        private void OnQueueChanged()
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_closing == true)
                {
                    return;
                }
                EnqueueScheduled();
                QueueChanged?.Invoke();
            });
        }

        private async Task ExecuteQueuedAsync(Func<CancellationToken, Task> action, CancellationToken token)
        {
            Task work = await Dispatcher.UIThread.InvokeAsync<Task>(() => RunAsync(action, token));
            await work.ConfigureAwait(false);
        }

        private async Task RunAsync(Func<CancellationToken, Task> action, CancellationToken token)
        {
            CancellationTokenSource lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
            _workLifetime = lifetime;
            _progress = new Progress<WorkProgress>(progress =>
            {
                if (_closing == true)
                {
                    return;
                }
                if (lifetime != _workLifetime)
                {
                    return;
                }
                ProgressChanged?.Invoke(progress);
            });
            StudioWorkState state = StudioWorkState.Succeeded;
            Exception failure = null;
            try
            {
                Started?.Invoke();
                lifetime.Token.ThrowIfCancellationRequested();
                await action(lifetime.Token);
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested == true)
            {
                state = StudioWorkState.Cancelled;
            }
            catch (Exception error)
            {
                state = StudioWorkState.Failed;
                failure = error;
                LogHelper.Error(error);
            }
            finally
            {
                _workLifetime = null;
                _progress = null;
                lifetime.Dispose();
                Finished?.Invoke(state, failure);
            }
        }

        public async ValueTask DisposeAsync()
        {
            _closing = true;
            CancelScheduled();
            _queue.Changed -= OnQueueChanged;
            await _queue.DisposeAsync();
        }
    }
}
