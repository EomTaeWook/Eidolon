using Eidolon.Core.Domain;
using Eidolon.App.Localization;

namespace Eidolon.App.ViewModels
{
    public class GenerationBackendChoice : ObservableObject
    {
        public GenerationBackend Value { get; private set; }
        public string Label { get; private set; }

        public GenerationBackendChoice(GenerationBackend value, StringHelper strings)
        {
            Value = value;
            Localize(strings);
        }

        public void Localize(StringHelper strings)
        {
            string key = "EidolonText527";
            if (Value == GenerationBackend.Codex)
            {
                key = "EidolonText528";
            }
            Label = strings.GetString(key);
            Raise(nameof(Label));
        }
    }
}
