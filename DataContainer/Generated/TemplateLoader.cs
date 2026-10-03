using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using DataContainer;

namespace DataContainer.Generated
{
    public interface ITemplateDeserializer
    {
        IEnumerable<T> Deserialize<T>(string json) where T : TemplateBase, new();
    }
    public partial class TemplateLoader
    {
        public static void Load(string path, ITemplateDeserializer deserializer)
        {
            TemplateContainer<StringTemplate>.Load(path, "String.json", deserializer);
        }
        public static void Load(Func<string, string> funcLoadJson, ITemplateDeserializer deserializer)
        {
            TemplateContainer<StringTemplate>.Load("String.json", funcLoadJson, deserializer);
        }
        public static void MakeRefTemplate()
        {
            TemplateContainer<StringTemplate>.MakeRefTemplate();
            
            TemplateContainer<StringTemplate>.Combine();
        }
    }
}
