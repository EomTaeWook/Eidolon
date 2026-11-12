using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace Eidolon.Modeling
{
    public class BlenderInstaller
    {
        public const string Version = "4.5.16";
        private const string PackageUrl = "https://download.blender.org/release/Blender4.5/";
        private const long MaximumExpandedBytes = 2L * 1024 * 1024 * 1024;
        private readonly Func<string, string, string, IProgress<BlenderDownloadProgress>, CancellationToken, Task> _download;
        private readonly Func<ModelingProcessRequest, CancellationToken, Task> _runProcess;

        public BlenderInstaller(Func<string, string, string, IProgress<BlenderDownloadProgress>, CancellationToken, Task> download, Func<ModelingProcessRequest, CancellationToken, Task> runProcess)
        {
            ArgumentNullException.ThrowIfNull(download);
            ArgumentNullException.ThrowIfNull(runProcess);
            _download = download;
            _runProcess = runProcess;
        }

        public static bool IsPlatformSupported
        {
            get
            {
                return GetPackage().FileName.Length != 0;
            }
        }

        public string GetInstallDirectory(string engineInstallDirectory)
        {
            if (string.IsNullOrWhiteSpace(engineInstallDirectory) == true)
            {
                return string.Empty;
            }
            if (Path.IsPathFullyQualified(engineInstallDirectory) == false)
            {
                return string.Empty;
            }
            try
            {
                return Path.GetFullPath(Path.Combine(engineInstallDirectory, "Blender", "Blender-" + Version));
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
            catch (PathTooLongException)
            {
                return string.Empty;
            }
        }

        public string GetInstalledExecutable(string engineInstallDirectory)
        {
            if (IsPlatformSupported == false)
            {
                return string.Empty;
            }
            string installDirectory = GetInstallDirectory(engineInstallDirectory);
            if (installDirectory.Length == 0)
            {
                return string.Empty;
            }
            string executable = GetExecutablePath(installDirectory);
            try
            {
                EnsurePathHasNoLinks(executable);
                FileInfo file = new(executable);
                if (file.Exists == true)
                {
                    if (file.Length > 0)
                    {
                        return executable;
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return string.Empty;
        }

        public async Task InstallAsync(string engineInstallDirectory, IProgress<BlenderDownloadProgress> progress, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            string installDirectory = GetInstallDirectory(engineInstallDirectory);
            if (installDirectory.Length == 0)
            {
                throw new ArgumentException("A valid absolute engine installation directory is required for Blender.", nameof(engineInstallDirectory));
            }
            (string fileName, string checksum) = GetPackage();
            if (fileName.Length == 0)
            {
                throw new PlatformNotSupportedException();
            }
            EnsurePathHasNoLinks(installDirectory);
            if (GetInstalledExecutable(engineInstallDirectory).Length != 0)
            {
                return;
            }
            if (Path.Exists(installDirectory) == true)
            {
                throw new IOException("The Blender installation directory is already occupied: " + installDirectory);
            }

            string blenderDirectory = Path.GetDirectoryName(installDirectory);
            string archivePath = Path.Combine(blenderDirectory, "Downloads", fileName);
            EnsurePathHasNoLinks(archivePath);
            EnsurePathHasNoLinks(archivePath + ".part");
            await _download(PackageUrl + fileName, archivePath, checksum, progress, token).ConfigureAwait(false);
            EnsurePathHasNoLinks(archivePath);
            await using FileStream archive = File.OpenRead(archivePath);
            byte[] archiveHash = await SHA256.HashDataAsync(archive, token).ConfigureAwait(false);
            if (Convert.ToHexStringLower(archiveHash) != checksum)
            {
                throw new InvalidDataException("The Blender archive checksum does not match: " + fileName);
            }
            archive.Position = 0;
            progress?.Report(new BlenderDownloadProgress(0, true));

            string installationId = Guid.NewGuid().ToString("N");
            string stagingDirectory = installDirectory + ".staging-" + installationId;
            EnsurePathHasNoLinks(stagingDirectory);
            if (Path.Exists(stagingDirectory) == true)
            {
                throw new IOException("The Blender staging directory is already occupied: " + stagingDirectory);
            }
            Directory.CreateDirectory(stagingDirectory);
            try
            {
                string packageDirectory = stagingDirectory;
                if (OperatingSystem.IsWindows() == true)
                {
                    await ExtractWindowsAsync(archive, stagingDirectory, token).ConfigureAwait(false);
                    if (File.Exists(GetExecutablePath(packageDirectory)) == false)
                    {
                        string[] directories = Directory.GetDirectories(stagingDirectory);
                        if (directories.Length == 1)
                        {
                            packageDirectory = directories[0];
                        }
                    }
                }
                else
                {
                    string logDirectory = Path.Combine(blenderDirectory, "Logs", "BlenderInstall-" + installationId);
                    await CopyMacBundleAsync(archivePath, stagingDirectory, logDirectory, token).ConfigureAwait(false);
                }
                string stagedExecutable = GetExecutablePath(packageDirectory);
                EnsurePathHasNoLinks(stagedExecutable);
                FileInfo executableFile = new(stagedExecutable);
                if (executableFile.Exists == false)
                {
                    throw new InvalidDataException("The Blender archive does not contain its executable: " + fileName);
                }
                if (executableFile.Length == 0)
                {
                    throw new InvalidDataException("The Blender archive contains an empty executable: " + fileName);
                }
                EnsurePathHasNoLinks(installDirectory);
                token.ThrowIfCancellationRequested();
                Directory.Move(packageDirectory, installDirectory);
            }
            finally
            {
                if (Directory.Exists(stagingDirectory) == true)
                {
                    EnsurePathHasNoLinks(stagingDirectory);
                    Directory.Delete(stagingDirectory, true);
                }
            }
        }

        private static string GetExecutablePath(string directory)
        {
            if (OperatingSystem.IsMacOS() == true)
            {
                return Path.Combine(directory, "Blender.app", "Contents", "MacOS", "Blender");
            }
            return Path.Combine(directory, "blender.exe");
        }

        private static (string FileName, string Checksum) GetPackage()
        {
            if (OperatingSystem.IsWindows() == true)
            {
                if (RuntimeInformation.ProcessArchitecture == Architecture.X64)
                {
                    return ("blender-" + Version + "-windows-x64.zip", "e258e90a1ebc14798ad65b3426bc5f11f5e0e013541c61cc7c3ee9bbf4ca0ce6");
                }
            }
            if (OperatingSystem.IsMacOS() == true)
            {
                if (RuntimeInformation.OSArchitecture == Architecture.Arm64)
                {
                    return ("blender-" + Version + "-macos-arm64.dmg", "c5cd40979f30033bc55ca84fccd00f2c1ddebb1d2f41618f220ef1f596d8b0f6");
                }
                if (RuntimeInformation.OSArchitecture == Architecture.X64)
                {
                    return ("blender-" + Version + "-macos-x64.dmg", "009e437b33e5e131340b8d91eb9005d69ef6f1c67eff1402953a7d40cf94638f");
                }
            }
            return (string.Empty, string.Empty);
        }

        private static async Task ExtractWindowsAsync(Stream archiveStream, string stagingDirectory, CancellationToken token)
        {
            using ZipArchive archive = new(archiveStream, ZipArchiveMode.Read, leaveOpen: true);
            string stagingPrefix = stagingDirectory + Path.DirectorySeparatorChar;
            long expandedBytes = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                token.ThrowIfCancellationRequested();
                if (((entry.ExternalAttributes >> 16) & 0xf000) == 0xa000)
                {
                    throw new InvalidDataException("The Blender archive contains a symbolic link.");
                }
                if ((entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                {
                    throw new InvalidDataException("The Blender archive contains a reparse point.");
                }
                if (entry.FullName.Contains(':') == true)
                {
                    throw new InvalidDataException("The Blender archive contains an invalid entry path.");
                }
                string path = Path.GetFullPath(Path.Combine(stagingDirectory, entry.FullName));
                if (path.StartsWith(stagingPrefix, StringComparison.OrdinalIgnoreCase) == false)
                {
                    throw new InvalidDataException("The Blender archive contains an entry outside the staging directory.");
                }
                long entryLength = entry.Length;
                if (entryLength < 0)
                {
                    throw new InvalidDataException("The Blender archive contains an invalid entry size.");
                }
                if (entryLength > MaximumExpandedBytes - expandedBytes)
                {
                    throw new InvalidDataException("The Blender archive is too large to extract.");
                }
                expandedBytes += entryLength;
                if (entry.Name.Length == 0)
                {
                    Directory.CreateDirectory(path);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                await using Stream input = entry.Open();
                await using FileStream output = new(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true);
                await input.CopyToAsync(output, token).ConfigureAwait(false);
            }
        }

        private async Task CopyMacBundleAsync(string archivePath, string stagingDirectory, string logDirectory, CancellationToken token)
        {
            string mountDirectory = stagingDirectory + ".mount";
            EnsurePathHasNoLinks(mountDirectory);
            EnsurePathHasNoLinks(logDirectory);
            if (Path.Exists(mountDirectory) == true)
            {
                throw new IOException("The Blender mount directory is already occupied: " + mountDirectory);
            }
            Directory.CreateDirectory(logDirectory);
            Directory.CreateDirectory(mountDirectory);
            string applicationBundle = Path.Combine(mountDirectory, "Blender.app");
            bool attachCompleted = false;
            try
            {
                ModelingProcessRequest attachRequest = new("/usr/bin/hdiutil", new string[] { "attach", "-readonly", "-nobrowse", "-noautoopen", "-mountpoint", mountDirectory, archivePath }, logDirectory, Path.Combine(logDirectory, "Attach.log"), null);
                await _runProcess(attachRequest, token).ConfigureAwait(false);
                attachCompleted = true;
                EnsurePathHasNoLinks(applicationBundle);
                if (File.Exists(GetExecutablePath(mountDirectory)) == false)
                {
                    throw new InvalidDataException("The Blender disk image does not contain Blender.app.");
                }
                ModelingProcessRequest copyRequest = new("/usr/bin/ditto", new string[] { applicationBundle, Path.Combine(stagingDirectory, "Blender.app") }, logDirectory, Path.Combine(logDirectory, "Copy.log"), null);
                await _runProcess(copyRequest, token).ConfigureAwait(false);
            }
            finally
            {
                await DetachMacImageAsync(mountDirectory, logDirectory, attachCompleted).ConfigureAwait(false);
                if (Directory.Exists(mountDirectory) == true)
                {
                    Directory.Delete(mountDirectory, false);
                }
            }
        }

        private async Task DetachMacImageAsync(string mountDirectory, string logDirectory, bool attachCompleted)
        {
            try
            {
                using CancellationTokenSource detachTimeout = new(TimeSpan.FromSeconds(30));
                ModelingProcessRequest detachRequest = new("/usr/bin/hdiutil", new string[] { "detach", mountDirectory }, logDirectory, Path.Combine(logDirectory, "Detach.log"), null);
                await _runProcess(detachRequest, detachTimeout.Token).ConfigureAwait(false);
            }
            catch
            {
                if (Directory.Exists(mountDirectory) == false)
                {
                    return;
                }
                if (attachCompleted == false)
                {
                    if (Directory.EnumerateFileSystemEntries(mountDirectory).Any() == false)
                    {
                        return;
                    }
                }
                using CancellationTokenSource forceTimeout = new(TimeSpan.FromSeconds(30));
                ModelingProcessRequest forceDetachRequest = new("/usr/bin/hdiutil", new string[] { "detach", "-force", mountDirectory }, logDirectory, Path.Combine(logDirectory, "DetachForce.log"), null);
                await _runProcess(forceDetachRequest, forceTimeout.Token).ConfigureAwait(false);
            }
        }

        internal static void EnsurePathHasNoLinks(string path)
        {
            string currentPath = Path.GetFullPath(path);
            while (string.IsNullOrEmpty(currentPath) == false)
            {
                try
                {
                    if ((File.GetAttributes(currentPath) & FileAttributes.ReparsePoint) != 0)
                    {
                        throw new IOException("The Blender path contains a link: " + currentPath);
                    }
                }
                catch (FileNotFoundException)
                {
                }
                catch (DirectoryNotFoundException)
                {
                }
                currentPath = Path.GetDirectoryName(currentPath);
            }
        }
    }
}
