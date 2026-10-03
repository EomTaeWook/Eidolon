using Dignus.Log;
using Dignus.Log.Formatting;
using Dignus.Log.LogTarget;
using Dignus.Log.Model;

namespace Eidolon.Core.Infrastructure
{
    public class ProcessLogFile : IDisposable
    {
        private readonly FileLogTarget _target;
        private readonly string _name;

        public ProcessLogFile(string path)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            _name = Path.GetFileNameWithoutExtension(path);
            _target = new FileLogTarget
            {
                LogFileName = path,
                ArchiveFileName = path + ".{#}",
                ArchiveRollingType = FileRollingType.Day,
                MaxArchiveFile = 7,
                AutoFlush = true,
                KeepOpenFile = true,
                LogFormatRenderer = new LogFormatRenderer("${datetime} | ${message}")
            };
        }

        public void WriteLine(string line)
        {
            _target.Write(new LogEvent(LogLevel.Info, line, string.Empty, 0, _name));
        }

        public void Dispose()
        {
            _target.Dispose();
        }
    }
}
