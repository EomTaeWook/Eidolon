using Eidolon.App.Localization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Dignus.DependencyInjection.Attributes;

namespace Eidolon.App.Services
{
    [Injectable(Dignus.DependencyInjection.LifeScope.Singleton)]
    public class DesktopDialogs
    {
        private readonly IClassicDesktopStyleApplicationLifetime _desktop;
        private readonly StringHelper _strings;

        public DesktopDialogs(IClassicDesktopStyleApplicationLifetime desktop, StringHelper strings)
        {
            _desktop = desktop;
            _strings = strings;
        }

        public async Task<string> PickFolderAsync(string title)
        {
            IReadOnlyList<IStorageFolder> folders = await _desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(
                new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            if (folders.Count == 0)
            {
                return string.Empty;
            }
            string path = folders[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path) == true)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText202"));
            }
            return path;
        }

        public async Task<string> PickModelAsync()
        {
            IReadOnlyList<IStorageFile> files = await _desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _strings.GetString("EidolonText203"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Safetensors") { Patterns = new[] { "*.safetensors" } } }
            });
            if (files.Count == 0)
            {
                return string.Empty;
            }
            string path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path) == true)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText204"));
            }
            return path;
        }

        public async Task ExportImageAsync(string source)
        {
            IStorageFile target = await _desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = _strings.GetString("EidolonText205"),
                SuggestedFileName = "Eidolon-" + Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(source))) + ".png",
                DefaultExtension = "png",
                FileTypeChoices = new[] { FilePickerFileTypes.ImagePng },
                ShowOverwritePrompt = true
            });
            if (target == null)
            {
                return;
            }
            string targetPath = target.TryGetLocalPath();
            if (targetPath != null && Path.GetFullPath(targetPath).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase) == true)
            {
                return;
            }
            await using Stream input = File.OpenRead(source);
            await using Stream output = await target.OpenWriteAsync();
            if (output.CanSeek == true)
            {
                output.SetLength(0);
            }
            await input.CopyToAsync(output);
        }

        public async Task OpenFolderAsync(string path)
        {
            Directory.CreateDirectory(path);
            if (await _desktop.MainWindow.Launcher.LaunchDirectoryInfoAsync(new DirectoryInfo(path)) == false)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText206"));
            }
        }

        public async Task OpenLinkAsync(string url)
        {
            if (await _desktop.MainWindow.Launcher.LaunchUriAsync(new Uri(url)) == false)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText207"));
            }
        }
    }
}
