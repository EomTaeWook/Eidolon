using System.Text.Json;
using System.Text.Json.Serialization;
using DataContainer.Generated;

namespace Eidolon.App
{
    public class TemplateDeserializer : ITemplateDeserializer
    {
        private readonly JsonSerializerOptions _serializerOptions;

        public TemplateDeserializer()
        {
            _serializerOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = false
            };
            _serializerOptions.Converters.Add(new JsonStringEnumConverter());
        }

        public IEnumerable<T> Deserialize<T>(string json) where T : TemplateBase, new()
        {
            List<T> values = JsonSerializer.Deserialize<List<T>>(json, _serializerOptions);
            if (values == null)
            {
                throw new InvalidDataException("Template JSON could not be deserialized. templateType:" + typeof(T).FullName);
            }
            foreach (T value in values)
            {
                if (value == null)
                {
                    throw new InvalidDataException("A template entry is null. templateType:" + typeof(T).FullName);
                }
                if (value.Invalid() == true)
                {
                    throw new InvalidDataException("A template ID is invalid. templateType:" + typeof(T).FullName);
                }
                if (string.IsNullOrWhiteSpace(value.Name) == true)
                {
                    throw new InvalidDataException("A template name is missing. id:" + value.Id);
                }
                if (value is StringTemplate entry)
                {
                    if (string.IsNullOrWhiteSpace(entry.Kor) == true)
                    {
                        throw new InvalidDataException("A Korean translation is missing: " + entry.Name);
                    }
                    if (string.IsNullOrWhiteSpace(entry.Eng) == true)
                    {
                        throw new InvalidDataException("An English translation is missing: " + entry.Name);
                    }
                }
            }
            return values;
        }
    }
}
