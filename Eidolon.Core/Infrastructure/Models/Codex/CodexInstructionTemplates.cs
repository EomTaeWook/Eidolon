namespace Eidolon.Core.Infrastructure
{
    public class CodexInstructionTemplates
    {
        public CodexInstructionTemplates(string request, string transparentBackground, string restyle, string reimagine, string inpaint)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(request);
            ArgumentException.ThrowIfNullOrWhiteSpace(transparentBackground);
            ArgumentException.ThrowIfNullOrWhiteSpace(restyle);
            ArgumentException.ThrowIfNullOrWhiteSpace(reimagine);
            ArgumentException.ThrowIfNullOrWhiteSpace(inpaint);
            Request = request;
            TransparentBackground = transparentBackground.TrimEnd('\r', '\n');
            Restyle = restyle.TrimEnd('\r', '\n');
            Reimagine = reimagine.TrimEnd('\r', '\n');
            Inpaint = inpaint.TrimEnd('\r', '\n');
        }

        public string Request { get; private set; }
        public string TransparentBackground { get; private set; }
        public string Restyle { get; private set; }
        public string Reimagine { get; private set; }
        public string Inpaint { get; private set; }
    }
}
