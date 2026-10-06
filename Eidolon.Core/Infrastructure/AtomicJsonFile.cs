using Eidolon.Core.Application;
using System.Text.Json;
using System.Text.Json.Serialization;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class AtomicJsonFile
    {
        private readonly JsonSerializerOptions _options;

        public AtomicJsonFile()
        {
            _options = new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            };
        }

        public T Read<T>(string path) where T : class
        {
            if (File.Exists(path) == false)
            {
                return null;
            }
            try
            {
                using FileStream stream = File.OpenRead(path);
                T value = JsonSerializer.Deserialize<T>(stream, _options);
                if (value == null)
                {
                    throw new StudioException(StudioMessageCode.EmptyJsonDocument);
                }
                return value;
            }
            catch (JsonException error)
            {
                throw new StudioException(StudioMessageCode.JsonReadFailed, error, Path.GetFileName(path));
            }
        }

        public void Write<T>(string path, T value)
        {
            Write(path, value, true);
        }

        public void WriteNew<T>(string path, T value)
        {
            Write(path, value, false);
        }

        private void Write<T>(string path, T value, bool overwrite)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            string temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (FileStream stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write))
                {
                    JsonSerializer.Serialize(stream, value, _options);
                    stream.Flush(true);
                }
                if (overwrite == true && File.Exists(path) == true)
                {
                    File.Replace(temporaryPath, path, path + ".bak");
                }
                else
                {
                    File.Move(temporaryPath, path);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath) == true)
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
