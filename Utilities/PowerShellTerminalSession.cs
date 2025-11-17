using Microsoft.Win32.SafeHandles;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Configer.Utilities;

public sealed class PowerShellTerminalSession : IAsyncDisposable
{
    private readonly TerminalLaunchOptions _options;
    private SafeFileHandle? _inputWriteHandle;
    private SafeFileHandle? _outputReadHandle;
    private FileStream? _stdinStream;
    private FileStream? _stdoutStream;
    private IntPtr _pseudoConsole = IntPtr.Zero;
    private IntPtr _attrList = IntPtr.Zero;
    private PROCESS_INFORMATION _processInfo;
    private Task? _readLoopTask;
    private Task? _exitWaitTask;
    private CancellationTokenSource? _readingCts;
    private IntPtr _commandLineBuffer = IntPtr.Zero;

    public event EventHandler<string>? OutputReceived;
    public event EventHandler<int>? Exited;

    public PowerShellTerminalSession(TerminalLaunchOptions? options = null)
    {
        _options = options ?? TerminalLaunchOptions.CreateDefault();
    }

    public async Task StartAsync(ushort columns, ushort rows, CancellationToken cancellationToken = default)
    {
        if (_pseudoConsole != IntPtr.Zero)
        {
            throw new InvalidOperationException("会话已在运行。");
        }

        cancellationToken.ThrowIfCancellationRequested();

        var security = new SECURITY_ATTRIBUTES
        {
            nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
            bInheritHandle = 1,
            lpSecurityDescriptor = IntPtr.Zero
        };

        if (!CreatePipe(out var inputReadPipe, out var inputWritePipe, ref security, 0))
        {
            ThrowLastWin32Exception("CreatePipe (输入)");
        }

        if (!CreatePipe(out var outputReadPipe, out var outputWritePipe, ref security, 0))
        {
            ThrowLastWin32Exception("CreatePipe (输出)");
        }

        try
        {
            SetHandleInformation(inputWritePipe, HANDLE_FLAG_INHERIT, 0);
            SetHandleInformation(outputReadPipe, HANDLE_FLAG_INHERIT, 0);

            var size = new COORD { X = (short)Math.Max(columns, (ushort)80), Y = (short)Math.Max(rows, (ushort)25) };
            var hr = CreatePseudoConsole(size, inputReadPipe.DangerousGetHandle(), outputWritePipe.DangerousGetHandle(), 0, out _pseudoConsole);
            if (hr != 0)
            {
                ThrowLastWin32Exception("CreatePseudoConsole", hr);
            }

            _inputWriteHandle = DuplicateHandleForAsync(inputWritePipe);
            _outputReadHandle = DuplicateHandleForAsync(outputReadPipe);

            InitializeStartupInfo();

            var workingDirectory = string.IsNullOrWhiteSpace(_options.WorkingDirectory)
                ? AppContext.BaseDirectory
                : _options.WorkingDirectory;

            _commandLineBuffer = BuildCommandLine(_options);

            try
            {
                if (!CreateProcess(
                        lpApplicationName: null,
                        lpCommandLine: _commandLineBuffer,
                        lpProcessAttributes: IntPtr.Zero,
                        lpThreadAttributes: IntPtr.Zero,
                        bInheritHandles: false,
                        dwCreationFlags: EXTENDED_STARTUPINFO_PRESENT | CREATE_UNICODE_ENVIRONMENT,
                        lpEnvironment: IntPtr.Zero,
                        lpCurrentDirectory: workingDirectory,
                        lpStartupInfo: ref _startupInfo,
                        lpProcessInformation: out _processInfo))
                {
                    ThrowLastWin32Exception("CreateProcess");
                }
            }
            finally
            {
                if (_commandLineBuffer != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_commandLineBuffer);
                    _commandLineBuffer = IntPtr.Zero;
                }
            }

            _stdinStream = new FileStream(_inputWriteHandle, FileAccess.Write, 4096, false);
            _stdoutStream = new FileStream(_outputReadHandle, FileAccess.Read, 4096, false);
            _readingCts = new CancellationTokenSource();
            _readLoopTask = Task.Run(() => PumpStdOutAsync(_readingCts.Token));
            _exitWaitTask = WaitForExitAsync();
        }
        catch
        {
            await StopInternalAsync().ConfigureAwait(false);
            throw;
        }
        finally
        {
            inputReadPipe.Dispose();
            inputWritePipe.Dispose();
            outputReadPipe.Dispose();
            outputWritePipe.Dispose();
        }
    }

    public Task SendInputAsync(string data, CancellationToken cancellationToken = default)
    {
        if (_stdinStream == null)
        {
            return Task.CompletedTask;
        }

        var buffer = Encoding.UTF8.GetBytes(data);
        return _stdinStream.WriteAsync(buffer, cancellationToken).AsTask();
    }

    public Task SendCtrlCAsync(CancellationToken cancellationToken = default)
    {
        return SendInputAsync("\x03", cancellationToken);
    }

    public void Resize(ushort columns, ushort rows)
    {
        if (_pseudoConsole == IntPtr.Zero)
        {
            return;
        }

        var coord = new COORD { X = (short)Math.Max(columns, (ushort)20), Y = (short)Math.Max(rows, (ushort)5) };
        ResizePseudoConsole(_pseudoConsole, coord);
    }

    public async Task StopAsync(bool graceful = true)
    {
        if (_stdinStream != null && graceful)
        {
            try
            {
                await SendInputAsync("exit\r");
                return;
            }
            catch
            {
                // fall back to force
            }
        }

        if (_processInfo.hProcess != IntPtr.Zero)
        {
            TerminateProcess(_processInfo.hProcess, unchecked((uint)-1));
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            try
            {
                await StopAsync(graceful: false).ConfigureAwait(false);
            }
            catch
            {
                // ignore shutdown exceptions
            }

            await StopInternalAsync().ConfigureAwait(false);
        }
        finally
        {
            GC.SuppressFinalize(this);
        }
    }

    private async Task StopInternalAsync()
    {
        _readingCts?.Cancel();

        if (_readLoopTask != null)
        {
            try { await _readLoopTask.ConfigureAwait(false); }
            catch { }
        }

        if (_exitWaitTask != null)
        {
            try { await _exitWaitTask.ConfigureAwait(false); }
            catch { }
        }

        _stdinStream?.Dispose();
        _stdoutStream?.Dispose();
        _inputWriteHandle?.Dispose();
        _outputReadHandle?.Dispose();

        if (_processInfo.hProcess != IntPtr.Zero)
        {
            CloseHandle(_processInfo.hProcess);
            _processInfo.hProcess = IntPtr.Zero;
        }

        if (_processInfo.hThread != IntPtr.Zero)
        {
            CloseHandle(_processInfo.hThread);
            _processInfo.hThread = IntPtr.Zero;
        }

        if (_attrList != IntPtr.Zero)
        {
            DeleteProcThreadAttributeList(_attrList);
            Marshal.FreeHGlobal(_attrList);
            _attrList = IntPtr.Zero;
        }

        if (_pseudoConsole != IntPtr.Zero)
        {
            ClosePseudoConsole(_pseudoConsole);
            _pseudoConsole = IntPtr.Zero;
        }

        if (_commandLineBuffer != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_commandLineBuffer);
            _commandLineBuffer = IntPtr.Zero;
        }

        _readingCts?.Dispose();
        _readingCts = null;
    }

    private async Task PumpStdOutAsync(CancellationToken token)
    {
        if (_stdoutStream == null)
        {
            return;
        }

        var buffer = new byte[8192];
        while (!token.IsCancellationRequested)
        {
            int read;
            try
            {
                read = await _stdoutStream.ReadAsync(buffer.AsMemory(0, buffer.Length), token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch
            {
                break;
            }

            if (read == 0)
            {
                break;
            }

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            OutputReceived?.Invoke(this, text);
        }
    }

    private async Task WaitForExitAsync()
    {
        if (_processInfo.hProcess == IntPtr.Zero)
        {
            return;
        }

        await Task.Run(() => WaitForSingleObject(_processInfo.hProcess, INFINITE)).ConfigureAwait(false);
        if (GetExitCodeProcess(_processInfo.hProcess, out var exitCode))
        {
            Exited?.Invoke(this, exitCode);
        }
    }

    private void InitializeStartupInfo()
    {
        var size = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        _attrList = Marshal.AllocHGlobal(size);
        if (!InitializeProcThreadAttributeList(_attrList, 1, 0, ref size))
        {
            ThrowLastWin32Exception("InitializeProcThreadAttributeList");
        }

        if (!UpdateProcThreadAttribute(
                _attrList,
                0,
                (IntPtr)PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE,
                _pseudoConsole,
                (IntPtr)IntPtr.Size,
                IntPtr.Zero,
                IntPtr.Zero))
        {
            ThrowLastWin32Exception("UpdateProcThreadAttribute");
        }

        _startupInfo = new STARTUPINFOEX
        {
            StartupInfo = new STARTUPINFO
            {
                cb = Marshal.SizeOf<STARTUPINFOEX>()
            },
            lpAttributeList = _attrList
        };
    }

    private STARTUPINFOEX _startupInfo;

    private static SafeFileHandle DuplicateHandleForAsync(SafeHandle source)
    {
        if (source == null || source.IsInvalid)
        {
            throw new ArgumentException("源句柄无效", nameof(source));
        }

    if (!DuplicateHandle(
        GetCurrentProcess(),
        source,
        GetCurrentProcess(),
        out var duplicated,
        0,
        false,
        0x00000002))
        {
            ThrowLastWin32Exception("DuplicateHandle");
        }

        return duplicated;
    }

    private static IntPtr BuildCommandLine(TerminalLaunchOptions options)
    {
        var full = string.IsNullOrWhiteSpace(options.Arguments)
            ? options.Executable
            : $"\"{options.Executable}\" {options.Arguments}";
        return Marshal.StringToHGlobalUni(full);
    }

    private static void ThrowLastWin32Exception(string context, int hr = 0)
    {
        var exception = hr == 0 ? new System.ComponentModel.Win32Exception() : new System.ComponentModel.Win32Exception(hr);
        throw new InvalidOperationException($"{context} 失败：{exception.Message}", exception);
    }

    private const uint PROC_THREAD_ATTRIBUTE_PSEUDOCONSOLE = 0x00020016;
    private const uint EXTENDED_STARTUPINFO_PRESENT = 0x00080000;
    private const uint CREATE_UNICODE_ENVIRONMENT = 0x00000400;
    private const uint INFINITE = 0xFFFFFFFF;
    private const uint HANDLE_FLAG_INHERIT = 0x00000001;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(
        out SafeFileHandle hReadPipe,
        out SafeFileHandle hWritePipe,
        ref SECURITY_ATTRIBUTES lpPipeAttributes,
        int nSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DuplicateHandle(
        IntPtr hSourceProcessHandle,
        SafeHandle hSourceHandle,
        IntPtr hTargetProcessHandle,
        out SafeFileHandle lpTargetHandle,
        uint dwDesiredAccess,
        bool bInheritHandle,
        uint dwOptions);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetFileCompletionNotificationModes(SafeFileHandle FileHandle, byte Flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetHandleInformation(SafeFileHandle hObject, uint dwMask, uint dwFlags);

    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr hPC);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int CreatePseudoConsole(COORD size, IntPtr hInput, IntPtr hOutput, uint dwFlags, out IntPtr phPC);

    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr hPC, COORD size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool CreateProcess(
        string? lpApplicationName,
        IntPtr lpCommandLine,
        IntPtr lpProcessAttributes,
        IntPtr lpThreadAttributes,
        bool bInheritHandles,
        uint dwCreationFlags,
        IntPtr lpEnvironment,
        string? lpCurrentDirectory,
        ref STARTUPINFOEX lpStartupInfo,
        out PROCESS_INFORMATION lpProcessInformation);

    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr hObject);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr lpAttributeList, int dwAttributeCount, int dwFlags, ref int lpSize);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr lpAttributeList, uint dwFlags, IntPtr attribute, IntPtr lpValue, IntPtr cbSize, IntPtr lpPreviousValue, IntPtr lpReturnSize);

    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr lpAttributeList);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetExitCodeProcess(IntPtr hProcess, out int lpExitCode);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool TerminateProcess(IntPtr hProcess, uint uExitCode);

    [StructLayout(LayoutKind.Sequential)]
    private struct SECURITY_ATTRIBUTES
    {
        public int nLength;
        public IntPtr lpSecurityDescriptor;
        public int bInheritHandle;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct COORD
    {
        public short X;
        public short Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct STARTUPINFOEX
    {
        public STARTUPINFO StartupInfo;
        public IntPtr lpAttributeList;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct STARTUPINFO
    {
        public int cb;
        public IntPtr lpReserved;
        public IntPtr lpDesktop;
        public IntPtr lpTitle;
        public int dwX;
        public int dwY;
        public int dwXSize;
        public int dwYSize;
        public int dwXCountChars;
        public int dwYCountChars;
        public int dwFillAttribute;
        public int dwFlags;
        public short wShowWindow;
        public short cbReserved2;
        public IntPtr lpReserved2;
        public IntPtr hStdInput;
        public IntPtr hStdOutput;
        public IntPtr hStdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PROCESS_INFORMATION
    {
        public IntPtr hProcess;
        public IntPtr hThread;
        public int dwProcessId;
        public int dwThreadId;
    }
}

public sealed record TerminalLaunchOptions(string Executable, string Arguments, string WorkingDirectory)
{
    public static TerminalLaunchOptions CreateDefault()
    {
        return new TerminalLaunchOptions(
            Executable: "powershell.exe",
            Arguments: "-NoLogo -NoExit -ExecutionPolicy Bypass",
            WorkingDirectory: AppContext.BaseDirectory);
    }
}
