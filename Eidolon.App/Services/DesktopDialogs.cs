using Eidolon.App.Localization;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using Dignus.DependencyInjection.Attributes;
using Eidolon.Core.Application;
using Eidolon.Core.Domain;

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

        public async Task<string> PickReferenceImageAsync()
        {
            IReadOnlyList<IStorageFile> files = await _desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _strings.GetString("EidolonText440"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType(_strings.GetString("EidolonText440"))
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.webp" }
                } }
            });
            if (files.Count == 0)
            {
                return string.Empty;
            }
            string path = files[0].TryGetLocalPath();
            if (string.IsNullOrEmpty(path) == true)
            {
                throw new StudioException(StudioMessageCode.InvalidReferenceImage);
            }
            return path;
        }

        public async Task<IReadOnlyList<string>> PickTrainingImagesAsync(string title)
        {
            IReadOnlyList<IStorageFile> files = await _desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = title,
                AllowMultiple = true,
                FileTypeFilter = new[] { new FilePickerFileType(_strings.GetString("EidolonText140"))
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp" }
                } }
            });
            List<string> paths = new List<string>();
            foreach (IStorageFile file in files)
            {
                string path = file.TryGetLocalPath();
                if (string.IsNullOrEmpty(path) == true)
                {
                    throw new StudioException(StudioMessageCode.TrainingImagesMissing);
                }
                paths.Add(path);
            }
            return paths;
        }

        public async Task ExportImageAsync(string source)
        {
            IStorageFile target = await _desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = _strings.GetString("EidolonText205"),
                SuggestedFileName = Path.GetFileName(source),
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

        public async Task<bool> ConfirmDeleteAsync(string title, string message)
        {
            Window dialog = new Window
            {
                Title = title,
                Width = 420,
                SizeToContent = SizeToContent.Height,
                CanResize = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            Button delete = new Button { Content = _strings.GetString("EidolonText321") };
            Button cancel = new Button { Content = _strings.GetString("EidolonText322") };
            delete.Click += (sender, arguments) => dialog.Close(true);
            cancel.Click += (sender, arguments) => dialog.Close(false);
            StackPanel buttons = new StackPanel
            {
                Orientation = Avalonia.Layout.Orientation.Horizontal,
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                Spacing = 8
            };
            buttons.Children.Add(cancel);
            buttons.Children.Add(delete);
            StackPanel content = new StackPanel { Margin = new Avalonia.Thickness(24), Spacing = 20 };
            content.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            content.Children.Add(buttons);
            dialog.Content = content;
            return await dialog.ShowDialog<bool>(_desktop.MainWindow);
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
