using System;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace UsageOrbit.Platform
{
    internal sealed class JobObject : IDisposable
    {
        private IntPtr handle;
        public JobObject()
        {
            handle = CreateJobObject(IntPtr.Zero, null);
            if (handle == IntPtr.Zero) throw new Win32Exception();
            var limits = new ExtendedLimitInformation();
            limits.BasicLimitInformation.LimitFlags = 0x2000;
            if (!SetInformationJobObject(handle, 9, ref limits, (uint)Marshal.SizeOf(limits)))
            {
                Dispose();
                throw new Win32Exception();
            }
        }
        public void Assign(IntPtr process)
        {
            if (!AssignProcessToJobObject(handle, process)) throw new Win32Exception();
        }
        public void Dispose()
        {
            IntPtr old = System.Threading.Interlocked.Exchange(ref handle, IntPtr.Zero);
            if (old != IntPtr.Zero) CloseHandle(old);
        }
        [StructLayout(LayoutKind.Sequential)] private struct BasicLimits
        {
            public long ProcessUserTimeLimit, JobUserTimeLimit;
            public uint LimitFlags;
            public UIntPtr MinimumWorkingSetSize, MaximumWorkingSetSize;
            public uint ActiveProcessLimit;
            public UIntPtr Affinity;
            public uint PriorityClass, SchedulingClass;
        }
        [StructLayout(LayoutKind.Sequential)] private struct IoCounters
        {
            public ulong ReadOperationCount, WriteOperationCount, OtherOperationCount;
            public ulong ReadTransferCount, WriteTransferCount, OtherTransferCount;
        }
        [StructLayout(LayoutKind.Sequential)] private struct ExtendedLimitInformation
        {
            public BasicLimits BasicLimitInformation;
            public IoCounters IoInfo;
            public UIntPtr ProcessMemoryLimit, JobMemoryLimit, PeakProcessMemoryUsed, PeakJobMemoryUsed;
        }
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)] private static extern IntPtr CreateJobObject(IntPtr attributes, string name);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool SetInformationJobObject(IntPtr job, int infoClass, ref ExtendedLimitInformation info, uint length);
        [DllImport("kernel32.dll", SetLastError = true)] private static extern bool AssignProcessToJobObject(IntPtr job, IntPtr process);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr handle);
    }
}
