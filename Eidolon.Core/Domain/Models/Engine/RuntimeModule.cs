namespace Eidolon.Core.Domain
{
    public class RuntimeModule
    {
        public RuntimeModuleKind Kind { get; set; }
        public RuntimeModuleState State { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;

        public RuntimeModule()
        {
        }
    }
}
