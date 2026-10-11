using Avalonia;
using Dignus.Log;
using Eidolon.App.Services;
using System.Globalization;

namespace Eidolon.App
{
    public class Program
    {
        public Program()
        {
        }

        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length > 0)
            {
                if (args[0] == "--eidolon-update-helper")
                {
                    Environment.ExitCode = 1;
                    if (args.Length == 2)
                    {
                        Environment.ExitCode = ApplicationUpdateHelper.RunHelperAsync(args[1]).GetAwaiter().GetResult();
                    }
                    return;
                }
            }
            using Mutex appInstance = new Mutex(false, "Eidolon.Desktop." + Environment.UserName);
            bool ownsInstance;
            try
            {
                ownsInstance = appInstance.WaitOne(0);
            }
            catch (AbandonedMutexException)
            {
                ownsInstance = true;
            }
            if (ownsInstance == false)
            {
                return;
            }
            try
            {
                string logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Eidolon", "Logs");
                using DesktopLogging logging = new DesktopLogging(logDirectory);
                StartUpdateCleanup(args);
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                try
                {
                    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
                    if (Application.Current is App app)
                    {
                        app.CompleteUpdateAfterShutdownAsync().GetAwaiter().GetResult();
                    }
                }
                catch (Exception error)
                {
                    LogHelper.Fatal(error);
                    throw;
                }
                finally
                {
                    AppDomain.CurrentDomain.UnhandledException -= OnUnhandledException;
                    TaskScheduler.UnobservedTaskException -= OnUnobservedTaskException;
                }
            }
            finally
            {
                appInstance.ReleaseMutex();
            }
        }

        public static AppBuilder BuildAvaloniaApp()
        {
            Avalonia.Logging.Logger.Sink = new DesktopLogSink();
            TemplateDataLoader templateDataLoader = new TemplateDataLoader();
            templateDataLoader.Load();
            return AppBuilder.Configure<App>().UsePlatformDetect();
        }

        private static void StartUpdateCleanup(string[] args)
        {
            if (args.Length < 3)
            {
                return;
            }
            if (args[0] != "--eidolon-update-cleanup")
            {
                return;
            }
            if (int.TryParse(args[2], NumberStyles.None, CultureInfo.InvariantCulture, out int helperPid) == false)
            {
                return;
            }
            if (helperPid <= 0)
            {
                return;
            }
            _ = ApplicationUpdateHelper.CleanupAfterHelperAsync(args[1], helperPid);
        }

        private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs arguments)
        {
            if (arguments.ExceptionObject is Exception error)
            {
                LogHelper.Fatal(error);
            }
        }

        private static void OnUnobservedTaskException(object sender, UnobservedTaskExceptionEventArgs arguments)
        {
            LogHelper.Error(arguments.Exception);
        }
    }
}
