using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Logging;
using Dignus.Log;

namespace Eidolon.App.Services
{
    public class DesktopLogSink : ILogSink
    {
        public DesktopLogSink()
        {
        }

        public bool IsEnabled(LogEventLevel level, string area)
        {
            return level >= LogEventLevel.Warning;
        }

        public void Log(LogEventLevel level, string area, object source, string messageTemplate)
        {
            Write(level, area, messageTemplate);
        }

        public void Log(LogEventLevel level, string area, object source, string messageTemplate, params object[] propertyValues)
        {
            int index = 0;
            string message = Regex.Replace(messageTemplate, @"\{[^{}]+\}", match =>
            {
                if (index >= propertyValues.Length)
                {
                    return match.Value;
                }
                return Convert.ToString(propertyValues[index++], CultureInfo.InvariantCulture);
            });
            Write(level, area, message);
        }

        private void Write(LogEventLevel level, string area, string message)
        {
            string entry = $"Avalonia {area} [{level}]: {message}";
            if (level >= LogEventLevel.Error)
            {
                LogHelper.Error(entry);
            }
            else
            {
                LogHelper.Info(entry);
            }
        }
    }
}
