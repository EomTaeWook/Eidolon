using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Eidolon.App.Services
{
    public class WindowsApplicationUpdateInstaller : ApplicationUpdateInstaller
    {
        public WindowsApplicationUpdateInstaller(ApplicationUpdateEnvironment environment, ApplicationUpdateFiles files) : base(environment, files)
        {
        }

        internal override string CandidateFileName
        {
            get
            {
                return "verified.exe";
            }
        }
        internal override string HelperFileName
        {
            get
            {
                return "helper.exe";
            }
        }

        internal override bool MatchesAssetName(string name, string tag)
        {
            if (name == "Eidolon-" + tag + "-win-x64.exe")
            {
                return true;
            }
            return name == "Eidolon.exe";
        }

        protected override ApplicationUpdateMessageCode GetPlatformBlockedCode()
        {
            if (OperatingSystem.IsWindows() == false)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            if (RuntimeInformation.ProcessArchitecture != Architecture.X64)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            if (Path.GetExtension(Environment.ExecutablePath).Equals(".exe", StringComparison.OrdinalIgnoreCase) == false)
            {
                return ApplicationUpdateMessageCode.DeploymentRequired;
            }
            return ApplicationUpdateMessageCode.None;
        }

        internal override void ValidatePlatformManifest(ApplicationUpdateInstallManifest manifest)
        {
            if (Path.GetExtension(manifest.InstallPath).Equals(".exe", StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (string.Equals(Path.GetDirectoryName(manifest.InstallPath), manifest.WorkingDirectory, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
        }

        internal override async Task InstallAndRestartAsync(ApplicationUpdateInstallManifest manifest, string directory)
        {
            string backup = null;
            string incoming = null;
            bool replaced = false;
            try
            {
                Files.ValidatePlainFile(manifest.InstallPath);
                await Files.VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                string candidate = Path.Combine(directory, CandidateFileName);
                Files.ValidatePlainFile(candidate);
                if (new FileInfo(candidate).Length != manifest.CandidateSize)
                {
                    throw new ApplicationUpdateException(ApplicationUpdateMessageCode.DownloadIncomplete, null, Array.Empty<object>());
                }
                await Files.VerifyHashAsync(candidate, manifest.CandidateSha256, CancellationToken.None).ConfigureAwait(false);
                string unique = Guid.NewGuid().ToString("N");
                incoming = Path.Combine(manifest.WorkingDirectory, ".eidolon-update-" + unique + ".new");
                backup = Path.Combine(manifest.WorkingDirectory, ".eidolon-update-" + unique + ".backup");
                File.Copy(candidate, incoming, false);
                await Files.VerifyHashAsync(incoming, manifest.CandidateSha256, CancellationToken.None).ConfigureAwait(false);
                File.Replace(incoming, manifest.InstallPath, backup);
                replaced = true;
                StartInstalled(manifest, directory, ApplicationUpdateMessageCode.None);
                Files.TryDeleteFile(backup);
                backup = null;
            }
            catch (Exception exception)
            {
                ApplicationUpdateMessageCode failureCode = ApplicationUpdateMessageCode.ReplaceFailed;
                Exception failure = exception;
                if (replaced == true)
                {
                    failureCode = ApplicationUpdateMessageCode.LaunchFailed;
                    try
                    {
                        File.Replace(backup, manifest.InstallPath, null);
                        backup = null;
                    }
                    catch (Exception restorationException)
                    {
                        failureCode = ApplicationUpdateMessageCode.RollbackFailed;
                        failure = new AggregateException(exception, restorationException);
                    }
                }
                if (failureCode != ApplicationUpdateMessageCode.RollbackFailed)
                {
                    try
                    {
                        Files.ValidatePlainFile(manifest.InstallPath);
                        await Files.VerifyHashAsync(manifest.InstallPath, manifest.InstalledSha256, CancellationToken.None).ConfigureAwait(false);
                        StartInstalled(manifest, directory, failureCode);
                    }
                    catch (Exception restartException)
                    {
                        failure = new AggregateException(failure, restartException);
                    }
                }
                throw new ApplicationUpdateException(failureCode, failure, Array.Empty<object>());
            }
            finally
            {
                if (incoming != null)
                {
                    Files.TryDeleteFile(incoming);
                }
            }
        }
    }
}
