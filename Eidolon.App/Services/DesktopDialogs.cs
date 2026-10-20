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
            IReadOnlyList<IStorageFolder> folders = await _desktop.MainWindow.StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
            if (folders.Count == 0)
            {
                return string.Empty;
            }
            using IStorageFolder selectedFolder = folders[0];
            string path = selectedFolder.TryGetLocalPath();
            if (string.IsNullOrEmpty(path) == true)
            {
                throw new InvalidOperationException(_strings.GetString("EidolonText202"));
            }
            return path;
        }

        public async Task<string> PickCodexExecutableAsync()
        {
            IReadOnlyList<IStorageFile> files = await _desktop.MainWindow.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
            {
                Title = _strings.GetString("EidolonText532"),
                AllowMultiple = false,
                FileTypeFilter = new[] { new FilePickerFileType("Codex") { Patterns = new[] { "*.exe" } } }
            });
            if (files.Count == 0)
            {
                return string.Empty;
            }
            using IStorageFile executableFile = files[0];
            string path = executableFile.TryGetLocalPath();
            if (string.IsNullOrEmpty(path) == true)
            {
                throw new StudioException(StudioMessageCode.CodexExecutableNotFound);
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
            using IStorageFile referenceFile = files[0];
            string path = referenceFile.TryGetLocalPath();
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
            try
            {
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
            finally
            {
                foreach (IStorageFile file in files)
                {
                    file.Dispose();
                }
            }
        }

        public async Task ExportImageAsync(string source)
        {
            using IStorageFile target = await _desktop.MainWindow.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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
            if (targetPath != null)
            {
                if (Path.GetFullPath(targetPath).Equals(Path.GetFullPath(source), StringComparison.OrdinalIgnoreCase) == true)
                {
                    return;
                }
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
            Button delete = new Button { Content = _strings.GetString("EidolonText321"), MinWidth = 96 };
            delete.Classes.Add("primary");
            Button cancel = new Button { Content = _strings.GetString("EidolonText322"), MinWidth = 96 };
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
            StackPanel content = new StackPanel { Spacing = 24 };
            content.Children.Add(new TextBlock { Text = message, TextWrapping = Avalonia.Media.TextWrapping.Wrap });
            content.Children.Add(buttons);
            Border dialogContentBorder = new Border { Child = content, CornerRadius = new Avalonia.CornerRadius(0) };
            dialogContentBorder.Classes.Add("card");
            dialog.Content = dialogContentBorder;
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
