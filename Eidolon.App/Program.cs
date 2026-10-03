using Avalonia;
using Dignus.Log;
using Eidolon.App.Services;

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
                AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                try
                {
                    BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
