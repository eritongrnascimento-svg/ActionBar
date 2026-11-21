using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace BarraInteligente;

internal sealed class PseudoTerminal : IDisposable
{
    private readonly Channel<byte[]> input = Channel.CreateBounded<byte[]>(128);
    private readonly object gate = new();
    private IntPtr console;
    private IntPtr process;
    private bool closed;

    internal PseudoTerminal(short columns, short rows, Action<string> output, Action<uint> exited)
    {
        if (!CreatePipe(out var readInput, out var writeInput, IntPtr.Zero, 0)) throw new Win32Exception();
        using (readInput)
        {
            if (!CreatePipe(out var readOutput, out var writeOutput, IntPtr.Zero, 0))
            {
                writeInput.Dispose();
                throw new Win32Exception();
            }
            using (writeOutput)
            {
                try
                {
                    Marshal.ThrowExceptionForHR(CreatePseudoConsole(new(columns, rows), readInput, writeOutput, 0, out console));
                    process = StartShell(console);
                }
                catch
                {
                    readOutput.Dispose();
                    writeInput.Dispose();
                    if (console != IntPtr.Zero) ClosePseudoConsole(console);
                    throw;
                }
            }
            Worker(() => ReadOutput(readOutput, output));
            Worker(() => WriteInput(writeInput));
            Worker(() =>
            {
                WaitForSingleObject(process, uint.MaxValue);
                GetExitCodeProcess(process, out var code);
                Dispose();
                CloseHandle(process);
                exited(code);
            });
        }
    }

    private static void Worker(Action work) => _ = Task.Factory.StartNew(work, CancellationToken.None,
        TaskCreationOptions.LongRunning, TaskScheduler.Default);

    internal bool Send(string text) => input.Writer.TryWrite(Encoding.UTF8.GetBytes(text));

    internal void Resize(short columns, short rows)
    {
        // Resizing is marshaled off the dispatcher. ConPTY may write output during the operation.
        _ = Task.Run(() =>
        {
            lock (gate)
                if (!closed) ResizePseudoConsole(console, new(columns, rows));
        });
    }

    private static void ReadOutput(SafeFileHandle handle, Action<string> output)
    {
        using (handle)
        using (var stream = new FileStream(handle, FileAccess.Read))
        using (var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false))
        {
            try
            {
                var buffer = new char[8192];
                int length;
                while ((length = reader.Read(buffer, 0, buffer.Length)) > 0)
                    output(new string(buffer, 0, length));
            }
            catch (IOException) { }
            catch (OperationCanceledException) { }
        }
    }

    private void WriteInput(SafeFileHandle handle)
    {
        using (handle)
        using (var stream = new FileStream(handle, FileAccess.Write))
        {
            try
            {
                while (input.Reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
                    while (input.Reader.TryRead(out var bytes))
                    {
                        stream.Write(bytes);
                        stream.Flush();
                    }
            }
            catch (IOException) { }
        }
    }

    public void Dispose()
    {
        input.Writer.TryComplete();
        _ = Task.Run(() =>
        {
            lock (gate)
            {
                if (closed) return;
                closed = true;
                ClosePseudoConsole(console);
            }
        });
    }

    private static IntPtr StartShell(IntPtr pseudoConsole)
    {
        nuint size = 0;
        InitializeProcThreadAttributeList(IntPtr.Zero, 1, 0, ref size);
        if (size == 0) throw new Win32Exception();
        var attributes = Marshal.AllocHGlobal(checked((nint)size));
        try
        {
            if (!InitializeProcThreadAttributeList(attributes, 1, 0, ref size)) throw new Win32Exception();
            try
            {
                if (!UpdateProcThreadAttribute(attributes, 0, (nuint)0x00020016, pseudoConsole, (nuint)IntPtr.Size,
                        IntPtr.Zero, IntPtr.Zero)) throw new Win32Exception();
                var startup = new StartupInfoEx { Info = new StartupInfo { Size = Marshal.SizeOf<StartupInfoEx>() }, Attributes = attributes };
                var shell = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
                var command = new StringBuilder($"\"{shell}\" -NoLogo -NoProfile");
                if (!CreateProcess(shell, command, IntPtr.Zero, IntPtr.Zero, false, 0x00080000, IntPtr.Zero,
                        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ref startup, out var info))
                    throw new Win32Exception();
                CloseHandle(info.Thread);
                return info.Process;
            }
            finally { DeleteProcThreadAttributeList(attributes); }
        }
        finally { Marshal.FreeHGlobal(attributes); }
    }

    [StructLayout(LayoutKind.Sequential)]
    private readonly record struct Coordinate(short X, short Y);

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfo
    {
        internal int Size;
        internal IntPtr Reserved, Desktop, Title;
        internal int X, Y, XSize, YSize, XCount, YCount, Fill, Flags;
        internal short ShowWindow, ReservedSize;
        internal IntPtr ReservedData, StdInput, StdOutput, StdError;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct StartupInfoEx
    {
        internal StartupInfo Info;
        internal IntPtr Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ProcessInfo
    {
        internal IntPtr Process, Thread;
        internal uint ProcessId, ThreadId;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CreatePipe(out SafeFileHandle read, out SafeFileHandle write, IntPtr attributes, uint size);
    [DllImport("kernel32.dll")]
    private static extern int CreatePseudoConsole(Coordinate size, SafeFileHandle input, SafeFileHandle output, uint flags, out IntPtr console);
    [DllImport("kernel32.dll")]
    private static extern int ResizePseudoConsole(IntPtr console, Coordinate size);
    [DllImport("kernel32.dll")]
    private static extern void ClosePseudoConsole(IntPtr console);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool InitializeProcThreadAttributeList(IntPtr list, int count, uint flags, ref nuint size);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool UpdateProcThreadAttribute(IntPtr list, uint flags, nuint attribute, IntPtr value, nuint size, IntPtr previous, IntPtr returned);
    [DllImport("kernel32.dll")]
    private static extern void DeleteProcThreadAttributeList(IntPtr list);
    [DllImport("kernel32.dll", EntryPoint = "CreateProcessW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CreateProcess(string application, StringBuilder command, IntPtr processAttributes, IntPtr threadAttributes,
        bool inheritHandles, uint flags, IntPtr environment, string directory, ref StartupInfoEx startup, out ProcessInfo process);
    [DllImport("kernel32.dll")]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
    [DllImport("kernel32.dll")]
    private static extern bool GetExitCodeProcess(IntPtr handle, out uint code);
    [DllImport("kernel32.dll")]
    private static extern bool CloseHandle(IntPtr handle);
}

