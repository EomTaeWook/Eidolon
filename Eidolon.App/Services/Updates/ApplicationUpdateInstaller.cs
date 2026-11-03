using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Eidolon.App.Services
{
    public abstract class ApplicationUpdateInstaller
    {
        private readonly ApplicationUpdateEnvironment _environment;
        private readonly ApplicationUpdateFiles _files;

        public ApplicationUpdateInstaller(ApplicationUpdateEnvironment environment, ApplicationUpdateFiles files)
        {
            _environment = environment;
            _files = files;
        }

        internal abstract string CandidateFileName { get; }
        internal abstract string HelperFileName { get; }
        internal abstract bool MatchesAssetName(string name, string tag);
        protected abstract ApplicationUpdateMessageCode GetPlatformBlockedCode();
        internal abstract void ValidatePlatformManifest(ApplicationUpdateInstallManifest manifest);
        internal abstract Task InstallAndRestartAsync(ApplicationUpdateInstallManifest manifest, string directory);

        protected ApplicationUpdateEnvironment Environment
        {
            get
            {
                return _environment;
            }
        }

        protected ApplicationUpdateFiles Files
        {
            get
            {
                return _files;
            }
        }

        protected virtual Task PreparePackageAsync(ApplicationUpdateInstallManifest manifest, string directory, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        internal ApplicationUpdateMessageCode GetInstallBlockedCode()
        {
            if (_environment.PublishedSingleFile == false)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            if (Debugger.IsAttached == true)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            string path = _environment.ExecutablePath;
            if (string.IsNullOrWhiteSpace(path) == true)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            if (Path.IsPathFullyQualified(path) == false)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Equals("dotnet", StringComparison.OrdinalIgnoreCase) == true)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            string directory = Path.GetDirectoryName(path);
            if (File.Exists(Path.Combine(directory, "Eidolon.dll")) == true)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            if (File.Exists(Path.Combine(directory, "Eidolon.deps.json")) == true)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            return GetPlatformBlockedCode();
        }

        internal void ValidateInstallEnvironment()
        {
            ApplicationUpdateMessageCode blocked = GetInstallBlockedCode();
            if (blocked != ApplicationUpdateMessageCode.None)
            {
                throw new ApplicationUpdateException(blocked, null, Array.Empty<object>());
            }
        }

        public Task<ApplicationUpdateInstallPlan> PrepareAsync(VerifiedApplicationUpdate update, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(update);
            return Task.Run(() => PrepareCoreAsync(update, cancellationToken), cancellationToken);
        }

        private async Task<ApplicationUpdateInstallPlan> PrepareCoreAsync(VerifiedApplicationUpdate update, CancellationToken cancellationToken)
        {
            string directory = null;
            try
            {
                ValidateInstallEnvironment();
                _files.ValidateOwnedDirectory(update.Directory);
                _files.ValidatePlainFile(update.FilePath);
                await _files.VerifyHashAsync(update.FilePath, update.Release.Sha256, cancellationToken).ConfigureAwait(false);
                if (new FileInfo(update.FilePath).Length != update.Release.Size)
                {
                    throw new ApplicationUpdateException(ApplicationUpdateMessageCode.DownloadIncomplete, null, Array.Empty<object>());
                }
                string installed = Path.GetFullPath(_environment.ExecutablePath);
                _files.ValidatePlainFile(installed);
                using Process original = Process.GetCurrentProcess();
                string installedHash = await _files.GetHashAsync(installed, cancellationToken).ConfigureAwait(false);
                directory = _files.CreateOwnedDirectory();
                string candidate = Path.Combine(directory, CandidateFileName);
                File.Copy(update.FilePath, candidate, false);
                File.Copy(installed, Path.Combine(directory, HelperFileName), false);
                cancellationToken.ThrowIfCancellationRequested();
                ApplicationUpdateInstallManifest manifest = new ApplicationUpdateInstallManifest();
                manifest.OriginalPid = original.Id;
                manifest.OriginalStartTicks = original.StartTime.ToUniversalTime().Ticks;
                manifest.InstallPath = installed;
                manifest.InstalledSha256 = installedHash;
                manifest.CandidateSha256 = update.Release.Sha256;
                manifest.CandidateSize = update.Release.Size;
                manifest.Nonce = Guid.NewGuid().ToString("N");
                manifest.WorkingDirectory = Path.GetDirectoryName(installed);
                manifest.CandidateVersion = update.Release.Version;
                await PreparePackageAsync(manifest, directory, cancellationToken).ConfigureAwait(false);
                await File.WriteAllTextAsync(Path.Combine(directory, "install.json"), JsonSerializer.Serialize(manifest), cancellationToken).ConfigureAwait(false);
                return new ApplicationUpdateInstallPlan(directory, manifest.Nonce, update);
            }
            catch (OperationCanceledException)
            {
                if (directory != null)
                {
                    _files.DeleteOwnedDirectory(directory);
                }
                throw;
            }
            catch (Exception exception)
            {
                if (directory != null)
                {
                    _files.DeleteOwnedDirectory(directory);
                }
                if (exception is ApplicationUpdateException)
                {
                    throw;
                }
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, exception, Array.Empty<object>()) { ReleasePage = update.Release.ReleasePage };
            }
        }

        public Task StartHelperAsync(ApplicationUpdateInstallPlan plan, CancellationToken cancellationToken = default)
        {
            return Task.Run(() => StartHelperCoreAsync(plan, cancellationToken), cancellationToken);
        }

        private async Task StartHelperCoreAsync(ApplicationUpdateInstallPlan plan, CancellationToken cancellationToken)
        {
            try
            {
                _files.ValidateOwnedDirectory(plan.Directory);
                cancellationToken.ThrowIfCancellationRequested();
                ProcessStartInfo start = new ProcessStartInfo(Path.Combine(plan.Directory, HelperFileName));
                start.UseShellExecute = false;
                start.CreateNoWindow = true;
                start.WorkingDirectory = plan.Directory;
                start.ArgumentList.Add("--eidolon-update-helper");
                start.ArgumentList.Add(Path.Combine(plan.Directory, "install.json"));
                plan.Helper = Process.Start(start);
                if (plan.Helper == null)
                {
                    throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
                }
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                while (File.Exists(Path.Combine(plan.Directory, "ready")) == false)
                {
                    if (plan.Helper.HasExited == true)
                    {
                        throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
                    }
                    await Task.Delay(100, timeout.Token).ConfigureAwait(false);
                }
                cancellationToken.ThrowIfCancellationRequested();
            }
            catch (OperationCanceledException)
            {
                if (cancellationToken.IsCancellationRequested == true)
                {
                    throw;
                }
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            catch (ApplicationUpdateException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, exception, Array.Empty<object>());
            }
        }

        public void EnsureHelperReady(ApplicationUpdateInstallPlan plan)
        {
            ArgumentNullException.ThrowIfNull(plan);
            _files.ValidateOwnedDirectory(plan.Directory);
            if (plan.Helper == null)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (plan.Helper.HasExited == true)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
        }

        public Task AuthorizeAfterShutdownAsync(ApplicationUpdateInstallPlan plan)
        {
            return Task.Run(() => AuthorizeAfterShutdown(plan));
        }

        private void AuthorizeAfterShutdown(ApplicationUpdateInstallPlan plan)
        {
            EnsureHelperReady(plan);
            File.WriteAllText(Path.Combine(plan.Directory, "authorized"), plan.Nonce);
            // The helper owns its separately verified copy. Keep the UI download on declined
            // shutdown, but remove it after the accepted handoff so it is not orphaned.
            try
            {
                _files.DeleteOwnedDownload(plan.Download);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (ApplicationUpdateException)
            {
                // A refused temporary cleanup does not revoke the accepted installation.
            }
            plan.Helper?.Dispose();
        }

        public Task CancelAsync(ApplicationUpdateInstallPlan plan)
        {
            return Task.Run(() => CancelCoreAsync(plan));
        }

        private async Task CancelCoreAsync(ApplicationUpdateInstallPlan plan)
        {
            if (plan == null)
            {
                return;
            }
            try
            {
                _files.ValidateOwnedDirectory(plan.Directory);
                File.WriteAllText(Path.Combine(plan.Directory, "cancel"), string.Empty);
                if (plan.Helper != null)
                {
                    using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
                    await plan.Helper.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                }
                _files.DeleteOwnedDirectory(plan.Directory);
                _files.DeleteOwnedDownload(plan.Download);
            }
            catch (IOException)
            {
                // Only this request's temporary files may be left; never touch the installation.
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (OperationCanceledException)
            {
                // Do not kill a helper or the original application to finish cleanup.
            }
            finally
            {
                plan.Helper?.Dispose();
            }
        }

        protected static void StartInstalled(ApplicationUpdateInstallManifest manifest, string directory, ApplicationUpdateMessageCode failureCode)
        {
            ProcessStartInfo start = new ProcessStartInfo(manifest.InstallPath);
            start.UseShellExecute = false;
            start.WorkingDirectory = manifest.WorkingDirectory;
            start.ArgumentList.Add("--eidolon-update-cleanup");
            start.ArgumentList.Add(directory);
            start.ArgumentList.Add(System.Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture));
            if (failureCode != ApplicationUpdateMessageCode.None)
            {
                start.ArgumentList.Add(failureCode.ToString());
            }
            using Process relaunched = Process.Start(start);
            if (relaunched == null)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.LaunchFailed, null, Array.Empty<object>());
            }
        }

    }
}
