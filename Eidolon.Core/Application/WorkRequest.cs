namespace Eidolon.Core.Application
{
    internal class WorkRequest
    {
        private readonly TaskCompletionSource<bool> _cancelRequested = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        private Task _cancellation = Task.CompletedTask;
        private readonly Action _discarded;

        public string Title { get; }
        public Func<CancellationToken, Task> Execute { get; }

        public WorkRequest(string title, Func<CancellationToken, Task> execute, Action discarded = null)
        {
            Title = title;
            Execute = execute;
            _discarded = discarded;
        }

        public void Discard()
        {
            try
            {
                _discarded?.Invoke();
            }
            catch (Exception error)
            {
                Dignus.Log.LogHelper.Error(error);
            }
        }

        public void StartCancellation(CancellationTokenSource lifetime)
        {
            _cancellation = WaitForCancellationAsync(lifetime);
        }

        public Task CancelAsync()
        {
            _cancelRequested.TrySetResult(true);
            return _cancellation;
        }

        public async Task FinishAsync()
        {
            _cancelRequested.TrySetResult(false);
            await _cancellation.ConfigureAwait(false);
        }

        private async Task WaitForCancellationAsync(CancellationTokenSource lifetime)
        {
            if (await _cancelRequested.Task.ConfigureAwait(false) == true)
            {
                await lifetime.CancelAsync().ConfigureAwait(false);
            }
        }
    }
}
