using System.Runtime.InteropServices;

namespace Eidolon.App.Models.Windows
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct WindowsPropertyKey
    {
        public Guid FormatId;
        public uint PropertyId;
    }

    [StructLayout(LayoutKind.Explicit, Size = 24)]
    internal struct WindowsPropertyValue
    {
        [FieldOffset(0)]
        public ushort ValueType;

        [FieldOffset(8)]
        public IntPtr StringPointer;
    }
}
