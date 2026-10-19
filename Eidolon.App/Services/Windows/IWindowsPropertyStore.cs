using Eidolon.App.Models.Windows;
using System.Runtime.InteropServices;

namespace Eidolon.App.Services.Windows
{
    [ComImport]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IWindowsPropertyStore
    {
        [PreserveSig]
        int GetCount(out uint count);

        [PreserveSig]
        int GetAt(uint index, out WindowsPropertyKey propertyKey);

        [PreserveSig]
        int GetValue(ref WindowsPropertyKey propertyKey, out WindowsPropertyValue propertyValue);

        [PreserveSig]
        int SetValue(ref WindowsPropertyKey propertyKey, ref WindowsPropertyValue propertyValue);

        [PreserveSig]
        int Commit();
    }
}
