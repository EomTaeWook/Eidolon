using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Eidolon.Core.Domain;

namespace Eidolon.Core.Infrastructure
{
    public class WindowsProcessLifetime : IDisposable
    {
        private const uint KillOnJobClose = 0x00002000;
        private const int ExtendedLimitInformationClass = 9;
        private SafeFileHandle _job;

        public WindowsProcessLifetime(bool enabled)
        {
            if (enabled == false || OperatingSystem.IsWindows() == false)
            {
                return;
            }
            _job = CreateJobObject(IntPtr.Zero, null);
            if (_job.IsInvalid == true)
            {
                int error = Marshal.GetLastWin32Error();
                _job.Dispose();
                _job = null;
                throw new StudioException(StudioMessageCode.ProcessStartFailed, new Win32Exception(error));
            }
            ExtendedLimitInformation limits = new ExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = KillOnJobClose;
            if (SetInformationJobObject(_job, ExtendedLimitInformationClass, ref limits,
                (uint)Marshal.SizeOf<ExtendedLimitInformation>()) == false)
            {
                int error = Marshal.GetLastWin32Error();
                Dispose();
                throw new StudioException(StudioMessageCode.ProcessStartFailed, new Win32Exception(error));
            }
        }

        public void Attach(Process process)
        {
            if (_job == null)
            {
                return;
            }
            if (AssignProcessToJobObject(_job, process.SafeHandle) == false)
            {
                throw new StudioException(StudioMessageCode.ProcessStartFailed,
                    new Win32Exception(Marshal.GetLastWin32Error()));
            }
        }

        public void Dispose()
        {
            _job?.Dispose();
            _job = null;
        }

        [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateJobObject(IntPtr attributes, string name);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetInformationJobObject(SafeFileHandle job, int informationClass,
            ref ExtendedLimitInformation information, uint length);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AssignProcessToJobObject(SafeFileHandle job, SafeProcessHandle process);

        [StructLayout(LayoutKind.Sequential)]
        private struct BasicLimitInformation
        {
            public long PerProcessUserTimeLimit;
            public long PerJobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize;
            public UIntPtr MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass;
            public uint SchedulingClass;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct IoCounters
        {
            public ulong ReadOperationCount;
            public ulong WriteOperationCount;
            public ulong OtherOperationCount;
            public ulong ReadTransferCount;
            public ulong WriteTransferCount;
            public ulong OtherTransferCount;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ExtendedLimitInformation
        {
            public BasicLimitInformation BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit;
            public UIntPtr JobMemoryLimit;
            public UIntPtr PeakProcessMemoryUsed;
            public UIntPtr PeakJobMemoryUsed;
        }
    }
}
