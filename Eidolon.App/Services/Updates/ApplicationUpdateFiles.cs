using System.Security.Cryptography;
using Dignus.DependencyInjection.Attributes;

namespace Eidolon.App.Services
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class ApplicationUpdateFiles
    {
        private static readonly string[] _ownedNames = { "download.partial", "verified.exe", "verified.zip", "helper.exe", "helper", "install.json", "ready", "authorized", "cancel" };
        private readonly string _updateRoot;

        public ApplicationUpdateFiles()
        {
            string temporaryDirectory = Path.GetFullPath(Path.GetTempPath());
            if (OperatingSystem.IsMacOS() == true)
            {
                // macOS exposes system aliases such as /var, while ProcessPath resolves them.
                // Resolve only the existing OS temp path before establishing our owned root.
                string resolvedDirectory = Path.GetPathRoot(temporaryDirectory);
                string relativeDirectory = Path.GetRelativePath(resolvedDirectory, temporaryDirectory);
                foreach (string component in relativeDirectory.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries))
                {
                    string componentPath = Path.Combine(resolvedDirectory, component);
                    FileSystemInfo linkTarget = Directory.ResolveLinkTarget(componentPath, true);
                    resolvedDirectory = componentPath;
                    if (linkTarget != null)
                    {
                        resolvedDirectory = linkTarget.FullName;
                    }
                }
                temporaryDirectory = resolvedDirectory;
            }
            _updateRoot = Path.Combine(temporaryDirectory, "Eidolon", "application-update");
        }

        internal string UpdateRoot
        {
            get
            {
                return _updateRoot;
            }
        }

        internal StringComparison PathComparison
        {
            get
            {
                if (OperatingSystem.IsWindows() == true)
                {
                    return StringComparison.OrdinalIgnoreCase;
                }
                return StringComparison.Ordinal;
            }
        }

        internal string CreateOwnedDirectory()
        {
            Directory.CreateDirectory(_updateRoot);
            ValidateNoLink(_updateRoot);
            ValidateNoLink(Path.GetDirectoryName(_updateRoot));
            string directory = Path.Combine(_updateRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            ValidateOwnedDirectory(directory);
            return directory;
        }

        internal void DeleteOwnedDownload(VerifiedApplicationUpdate update)
        {
            DeleteOwnedDirectory(update.Directory);
        }

        internal void DeleteOwnedDirectory(string directory)
        {
            if (Directory.Exists(directory) == false)
            {
                return;
            }
            ValidateOwnedDirectory(directory);
            foreach (string name in _ownedNames)
            {
                TryDeleteFile(Path.Combine(directory, name));
            }
            try
            {
                string bundle = Path.Combine(directory, "Eidolon.app");
                if (Directory.Exists(bundle) == true)
                {
                    DeletePlainDirectoryTree(bundle);
                }
                Directory.Delete(directory, false);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        internal void DeletePlainDirectoryTree(string directory)
        {
            ValidateNoLink(directory);
            foreach (string file in Directory.EnumerateFiles(directory))
            {
                ValidateNoLink(file);
                File.Delete(file);
            }
            foreach (string child in Directory.EnumerateDirectories(directory))
            {
                DeletePlainDirectoryTree(child);
            }
            Directory.Delete(directory, false);
        }

        internal async Task VerifyHashAsync(string path, string expected, CancellationToken cancellationToken)
        {
            string hash = await GetHashAsync(path, cancellationToken).ConfigureAwait(false);
            if (string.Equals(hash, expected, StringComparison.OrdinalIgnoreCase) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.DigestMismatch, null, Array.Empty<object>());
            }
        }

        internal async Task<string> GetHashAsync(string path, CancellationToken cancellationToken)
        {
            using FileStream file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            byte[] hash = await SHA256.HashDataAsync(file, cancellationToken).ConfigureAwait(false);
            return Convert.ToHexStringLower(hash);
        }

        internal void ValidateOwnedDirectory(string directory)
        {
            string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            string parent = Path.GetDirectoryName(full);
            if (string.Equals(parent, _updateRoot, PathComparison) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            if (Guid.TryParseExact(Path.GetFileName(full), "N", out Guid _) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            ValidateNoLink(full);
            ValidateNoLink(_updateRoot);
        }

        internal void ValidatePlainFile(string path)
        {
            if (File.Exists(path) == false)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
            ValidateNoLink(path);
            ValidateNoLink(Path.GetDirectoryName(path));
        }

        internal void ValidateNoLink(string path)
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
            {
                throw new ApplicationUpdateException(ApplicationUpdateMessageCode.PrepareFailed, null, Array.Empty<object>());
            }
        }

        internal void TryDeleteFile(string path)
        {
            try
            {
                File.Delete(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

    }
}
