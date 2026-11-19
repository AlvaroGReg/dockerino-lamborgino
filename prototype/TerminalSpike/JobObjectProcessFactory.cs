using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using EasyWindowsTerminalControl.Internals;
using Microsoft.Win32.SafeHandles;

namespace TerminalSpike;

/// <summary>
/// Starts ConPTY processes suspended, assigns them to a session Job Object,
/// and only then resumes the primary thread.
/// </summary>
internal sealed class JobObjectProcessFactory : IProcessFactory, IDisposable
{
    private const uint ExtendedStartupInfoPresent = 0x00080000;
    private const uint CreateSuspended = 0x00000004;
    private const int StartfUseStdHandles = 0x00000100;
    private const uint JobObjectExtendedLimitInformationClass = 9;
    private const uint JobObjectLimitKillOnJobClose = 0x00002000;
    private const uint Infinite = 0xFFFFFFFF;

    private readonly SafeKernelHandle _job;
    private bool _disposed;

    public JobObjectProcessFactory()
    {
        _job = CreateJobObjectW(IntPtr.Zero, null);
        if (_job.IsInvalid)
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo crear el Job Object.");
        }

        var limits = new JobObjectExtendedLimitInformation
        {
            BasicLimitInformation = new JobObjectBasicLimitInformation
            {
                LimitFlags = JobObjectLimitKillOnJobClose
            }
        };

        if (!SetInformationJobObject(
                _job,
                JobObjectExtendedLimitInformationClass,
                ref limits,
                (uint)Marshal.SizeOf<JobObjectExtendedLimitInformation>()))
        {
            var error = Marshal.GetLastWin32Error();
            _job.Dispose();
            throw new Win32Exception(error, "No se pudo configurar el Job Object.");
        }
    }

    public IProcess Start(string command, nuint attributes, PseudoConsole console, string? workingDirectory = null)
    {
        // Explicit NULL standard handles prevent the GUI host's redirected handles
        // from reaching the child. Windows supplies the pseudoconsole handles.
        var startupInfo = new StartupInfoEx
        {
            StartupInfo = new StartupInfo
            {
                Size = Marshal.SizeOf<StartupInfoEx>(),
                Flags = StartfUseStdHandles
            }
        };

        nuint attributeListSize = 0;
        _ = InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref attributeListSize);
        var attributeList = Marshal.AllocHGlobal((nint)attributeListSize);
        try
        {
            if (!InitializeProcThreadAttributeList(attributeList, 1, 0, ref attributeListSize))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo inicializar la lista de atributos de ConPTY.");
            }

            startupInfo.AttributeList = attributeList;
            var pseudoConsoleHandle = console.GetDangerousHandle;
            if (!UpdateProcThreadAttribute(
                    attributeList,
                    0,
                    (IntPtr)attributes,
                    pseudoConsoleHandle,
                    (nuint)IntPtr.Size,
                    IntPtr.Zero,
                    IntPtr.Zero))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo asociar el proceso a ConPTY.");
            }

            var commandLine = new StringBuilder(command);
            if (!CreateProcessW(
                    null,
                    commandLine,
                    IntPtr.Zero,
                    IntPtr.Zero,
                    true,
                    ExtendedStartupInfoPresent | CreateSuspended,
                    IntPtr.Zero,
                    workingDirectory,
                    ref startupInfo,
                    out var processInfo))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo crear el proceso de la terminal.");
            }

            try
            {
                if (!AssignProcessToJobObject(_job, processInfo.Process))
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo asignar el proceso al Job Object.");
                }

                if (ResumeThread(processInfo.Thread) == uint.MaxValue)
                {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo reanudar el proceso de la terminal.");
                }

                return new JobProcess(processInfo.Process, processInfo.Thread, processInfo.ProcessId, _job);
            }
            catch
            {
                _ = TerminateProcess(processInfo.Process, 1);
                _ = CloseHandle(processInfo.Process);
                _ = CloseHandle(processInfo.Thread);
                throw;
            }
        }
        finally
        {
            if (startupInfo.AttributeList != IntPtr.Zero)
            {
                DeleteProcThreadAttributeList(attributeList);
            }

            Marshal.FreeHGlobal(attributeList);
        }
    }

    public void TerminateSession()
    {
        if (!TerminateJobObject(_job, 1))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo detener el Job Object.");
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _job.Dispose();
    }

    internal interface IProcessDiagnostics
    {
        int ProcessId { get; }
        int? ExitCode { get; }
    }

    private sealed class JobProcess : IProcess, IProcessDiagnostics
    {
        private readonly SafeKernelHandle _processHandle;
        private readonly SafeKernelHandle _threadHandle;
        private readonly SafeKernelHandle _job;
        private readonly uint _processId;
        private readonly object _sync = new();
        private int? _finalExitCode;

        public JobProcess(IntPtr processHandle, IntPtr threadHandle, uint processId, SafeKernelHandle job)
        {
            _processHandle = new SafeKernelHandle(processHandle, ownsHandle: true);
            _threadHandle = new SafeKernelHandle(threadHandle, ownsHandle: true);
            _processId = processId;
            _job = job;
        }

        public bool HasExited => ExitCode.HasValue;
        public int ProcessId => unchecked((int)_processId);
        public int? ExitCode
        {
            get
            {
                lock (_sync)
                {
                    if (_finalExitCode.HasValue) return _finalExitCode;
                    var wait = WaitForSingleObject(_processHandle, 0);
                    if (wait == 258) return null; // WAIT_TIMEOUT
                    if (wait != 0 || !GetExitCodeProcess(_processHandle, out var exitCode))
                        throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo consultar el código de salida.");
                    return _finalExitCode = unchecked((int)exitCode);
                }
            }
        }

        public void WaitForExit()
        {
            if (WaitForSingleObject(_processHandle, Infinite) != 0)
                throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo esperar al proceso.");
        }

        public void Kill(bool EntireProcessTree = false)
        {
            if (!TerminateJobObject(_job, 1))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "No se pudo detener el Job Object.");
            }
        }

        public void Dispose()
        {
            lock (_sync)
            {
                _ = ExitCode;
                _threadHandle.Dispose();
                _processHandle.Dispose();
            }
        }
    }

    private sealed class SafeKernelHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        public SafeKernelHandle() : base(ownsHandle: true) { }

        public SafeKernelHandle(IntPtr handle, bool ownsHandle) : base(ownsHandle) => SetHandle(handle);

        protected override bool ReleaseHandle() => CloseHandle(handle);
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct StartupInfo
    {
        public int Size;
        public IntPtr Reserved;
        public IntPtr Desktop;
        public IntPtr Title;
        public int X;
        public int Y;
        public int XSize;
        public int YSize;
        public int XCountChars;
        public int YCountChars;
        public int FillAttribute;
        public int Flags;
        public short ShowWindow;
        public short Reserved2;
        public IntPtr Reserved2Pointer;
        public IntPtr StandardInput;
        public IntPtr StandardOutput;
        public IntPtr StandardError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        public StartupInfo StartupInfo;
        public IntPtr AttributeList;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInformation
    {
        public IntPtr Process;
        public IntPtr Thread;
        public uint ProcessId;
        public uint ThreadId;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JobObjectBasicLimitInformation
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
    private struct JobObjectExtendedLimitInformation
    {
        public JobObjectBasicLimitInformation BasicLimitInformation;
        public IoCounters IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryUsed;
        public UIntPtr PeakJobMemoryUsed;
    }

    [DllImport("kernel32.dll", EntryPoint = "CreateJobObjectW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeKernelHandle CreateJobObjectW(IntPtr jobAttributes, string? name);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetInformationJobObject(
        SafeKernelHandle job,
        uint informationClass,
        ref JobObjectExtendedLimitInformation information,
        uint informationLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InitializeProcThreadAttributeList(
        IntPtr attributeList,
        int attributeCount,
        uint flags,
        ref nuint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UpdateProcThreadAttribute(
        IntPtr attributeList,
        uint flags,
        IntPtr attribute,
        IntPtr value,
        nuint size,
        IntPtr previousValue,
        IntPtr returnSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern void DeleteProcThreadAttributeList(IntPtr attributeList);

    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CreateProcessW(
        string? applicationName,
        StringBuilder commandLine,
        IntPtr processAttributes,
        IntPtr threadAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool inheritHandles,
        uint creationFlags,
        IntPtr environment,
        string? currentDirectory,
        ref StartupInfoEx startupInfo,
        out ProcessInformation processInformation);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AssignProcessToJobObject(SafeKernelHandle job, IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint ResumeThread(IntPtr thread);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateJobObject(SafeKernelHandle job, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool TerminateProcess(IntPtr process, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeProcess(SafeKernelHandle process, out uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(SafeKernelHandle handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);
}
