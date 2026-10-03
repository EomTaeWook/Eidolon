using Dignus.Log;
using Dignus.Log.Formatting;
using Dignus.Log.LogTarget;
using Dignus.Log.Model;
using Dignus.Log.Rule;

namespace Eidolon.App.Services
{
    public class DesktopLogging : IDisposable
    {
        public DesktopLogging(string logDirectory)
        {
            Directory.CreateDirectory(logDirectory);
            FileLogTarget target = new FileLogTarget
            {
                LogFileName = Path.Combine(logDirectory, "Eidolon.log"),
                ArchiveFileName = Path.Combine(logDirectory, "Archive", "Eidolon.{#}.log"),
                ArchiveRollingType = FileRollingType.Day,
                MaxArchiveFile = 7,
                AutoFlush = true,
                KeepOpenFile = true,
                LogFormatRenderer = new LogFormatRenderer()
            };
            target.LogFormatRenderer.SetLogFormat("${datetime} | ${level} | ${message} | ${callerFileName} : ${callerLineNumber}");
            LogConfiguration configuration = new LogConfiguration();
            configuration.AddLogRule("EidolonFile", new LoggerRule("Eidolon", LogLevel.Debug, target));
            LogBuilder.Configuration(configuration).Build();
            LogHelper.SetLogger(LogManager.GetLogger("Eidolon"));
            LogHelper.Info("Eidolon started.");
        }

        public void Dispose()
        {
            LogHelper.Info("Eidolon stopped.");
            LogManager.Dispose();
        }
    }
}
