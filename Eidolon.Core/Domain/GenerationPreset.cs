namespace Eidolon.Core.Domain
{
    public class GenerationPreset
    {
        public int Resolution { get; private set; }
        public int Steps { get; private set; }
        public double Guidance { get; private set; }
        public string Sampler { get; private set; } = "dpmpp_2m";
        public string Scheduler { get; private set; } = "karras";

        public GenerationPreset(ModelFamily family)
        {
            switch (family)
            {
                case ModelFamily.StableDiffusion15:
                    Resolution = 512;
                    Steps = 28;
                    Guidance = 7;
                    break;
                case ModelFamily.Sdxl:
                    Resolution = 1024;
                    Steps = 30;
                    Guidance = 5;
                    break;
                default:
                    throw new StudioException(StudioMessageCode.UnsupportedGenerationFamily);
            }
        }
    }
}
