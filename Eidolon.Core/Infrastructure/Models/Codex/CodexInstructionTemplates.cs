namespace Eidolon.Core.Infrastructure
{
    public class CodexInstructionTemplates
    {
        public CodexInstructionTemplates(string request, string transparentBackground, string restyle, string reimagine)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(transparentBackground);
            ArgumentException.ThrowIfNullOrWhiteSpace(restyle);
            ArgumentException.ThrowIfNullOrWhiteSpace(reimagine);
            Request = request;
            TransparentBackground = transparentBackground.TrimEnd('\r', '\n');
            Restyle = restyle.TrimEnd('\r', '\n');
            Reimagine = reimagine.TrimEnd('\r', '\n');
        }

        public string Request { get; private set; }
        public string TransparentBackground { get; private set; }
        public string Restyle { get; private set; }
        public string Reimagine { get; private set; }
    }
}
