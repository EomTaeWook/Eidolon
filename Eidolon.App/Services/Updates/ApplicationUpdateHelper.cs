using System.Diagnostics;
using System.Text.Json;

namespace Eidolon.App.Services
{
    public class ApplicationUpdateHelper
    {
        private readonly ApplicationUpdateFiles _files;
        private readonly ApplicationUpdateInstaller _installer;

        public ApplicationUpdateHelper(ApplicationUpdateFiles files, ApplicationUpdateInstaller installer)
        {
            _files = files;
            _installer = installer;
        }

        public static Task<int> RunHelperAsync(string manifestPath)
        {
            ApplicationUpdateHelper helper = CreateHelper();
            return helper.RunAsync(manifestPath);
        }

        public static Task CleanupAfterHelperAsync(string directory, int helperPid)
        {
            ApplicationUpdateHelper helper = CreateHelper();
            return helper.CleanupAsync(directory, helperPid);
        }

        private static ApplicationUpdateHelper CreateHelper()
        {
            ApplicationUpdateFiles files = new ApplicationUpdateFiles();
            ApplicationUpdateEnvironment environment = App.CreateUpdateEnvironment();
            ApplicationUpdateInstaller installer;
            if (OperatingSystem.IsMacOS() == true)
            {
                installer = new MacOsApplicationUpdateInstaller(environment, files);
            }
            else
            {
                installer = new WindowsApplicationUpdateInstaller(environment, files);
            }
            return new ApplicationUpdateHelper(files, installer);
        }

        private async Task<int> RunAsync(string manifestPath)
        {
            string directory = null;
            ApplicationUpdateInstallManifest manifest = null;
            try
            {
                directory = Path.GetDirectoryName(Path.GetFullPath(manifestPath));
                _files.ValidateOwnedDirectory(directory);
                if (Path.GetFileName(manifestPath) != "install.json")
                {
                    return 1;
                }
                string helperPath = Path.Combine(directory, _installer.HelperFileName);
                if (string.Equals(Environment.ProcessPath, helperPath, _files.PathComparison) == false)
                {
                    return 1;
                }
                _files.ValidatePlainFile(manifestPath);
                if (new FileInfo(manifestPath).Length > 16384)
                {
                    return 1;
                }
                manifest = JsonSerializer.Deserialize<ApplicationUpdateInstallManifest>(await File.ReadAllTextAsync(manifestPath).ConfigureAwait(false));
                ValidateManifest(manifest, directory);
                await _files.VerifyHashAsync(helperPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                using Process original = Process.GetProcessById(manifest.OriginalPid);
                if (original.StartTime.ToUniversalTime().Ticks != manifest.OriginalStartTicks)
                {
                    return 1;
                }
                if (original.MainModule == null)
                {
                    return 1;
                }
                if (string.Equals(original.MainModule.FileName, manifest.InstallPath, _files.PathComparison) == false)
                {
                    return 1;
                }
                _files.ValidatePlainFile(manifest.InstallPath);
                await _files.VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                File.WriteAllText(Path.Combine(directory, "ready"), string.Empty);
                DateTime deadlineUtc = DateTime.UtcNow.AddMinutes(10);
                while (original.HasExited == false)
                {
                    if (File.Exists(Path.Combine(directory, "cancel")) == true)
                    {
                        return 0;
                    }
                    if (DateTime.UtcNow >= deadlineUtc)
                    {
                        return 1;
                    }
                    await Task.Delay(100).ConfigureAwait(false);
                }
                string authorization = Path.Combine(directory, "authorized");
                if (File.Exists(authorization) == false)
                {
                    return 0;
                }
                if (await File.ReadAllTextAsync(authorization).ConfigureAwait(false) != manifest.Nonce)
                {
                    return 1;
                }
                if (File.Exists(Path.Combine(directory, "cancel")) == true)
                {
                    return 0;
                }
                await _installer.InstallAndRestartAsync(manifest, directory).ConfigureAwait(false);
                return 0;
            }
            catch (Exception)
            {
                // Platform replacement owns restoration and relaunch after authorization.
                return 1;
            }
        }

        private Task CleanupAsync(string directory, int helperPid)
        {
            return Task.Run(async () =>
            {
                try
                {
                    _files.ValidateOwnedDirectory(directory);
                    if (helperPid == Environment.ProcessId)
                    {
                        return;
                    }
                    try
                    {
                        using Process helper = Process.GetProcessById(helperPid);
                        using CancellationTokenSource timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                        await helper.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
                    }
                    catch (ArgumentException)
                    {
                        // The helper has already exited.
                    }
                    _files.DeleteOwnedDirectory(directory);
                }
                catch (Exception)
                {
                    // Best-effort removal of this specific updater directory, without logging paths.
                }
            });
        }

        private void ValidateManifest(ApplicationUpdateInstallManifest manifest, string directory)
        {
            if (manifest == null)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (manifest.OriginalPid <= 0)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (manifest.OriginalPid == Environment.ProcessId)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (Path.IsPathFullyQualified(manifest.InstallPath) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            _installer.ValidatePlatformManifest(manifest);
            if (manifest.InstallPath.StartsWith(_files.UpdateRoot + Path.DirectorySeparatorChar, _files.PathComparison) == true)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (Guid.TryParseExact(manifest.Nonce, "N", out Guid _) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (ApplicationUpdateService.IsSha256(manifest.InstalledSha256) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (ApplicationUpdateService.IsSha256(manifest.CandidateSha256) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (manifest.CandidateSize <= 0)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            _files.ValidatePlainFile(Path.Combine(directory, _installer.CandidateFileName));
        }

    }
}
