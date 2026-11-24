using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.ExceptionServices;
using System.Text;
using EasyWindowsTerminalControl;
using EasyWindowsTerminalControl.Internals;
using Microsoft.Win32.SafeHandles;

namespace DockerinoLamborgino.Terminal;

/// <summary>
/// Coordinates TermPTY's output reader with pseudoconsole closure. The upstream
/// output pipe's writer remains open until Start returns, so EOF alone cannot
/// tell its inline reader that the pseudoconsole has finished writing.
/// </summary>
public sealed class DrainingTerminal : TermPTY
{
    private readonly TaskCompletionSource consoleClosed = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly ManualResetEventSlim readerFinished = new();
    private int readerStarted;
    private ExceptionDispatchInfo? readerError;
    public Exception? ReaderError => readerError?.SourceException;

    public IProcessFactory WrapFactory(IProcessFactory factory) => new ClosingFactory(this, factory);

    protected override void ReadOutputLoop()
    {
        // Both TermPTY.Start and the UI connection may invoke this method.
        // Whichever loses must wait: Start must not dispose its pipes early.
        if (Interlocked.Exchange(ref readerStarted, 1) != 0)
        {
            readerFinished.Wait();
            readerError?.Throw();
            return;
        }

        try
        {
            var stream = ConsoleOutStream;
            var decoder = Encoding.UTF8.GetDecoder();
            var bytes = new byte[16384];
            var chars = new char[Encoding.UTF8.GetMaxCharCount(bytes.Length)];
            var first = true;
            void Publish(int count)
            {
                var span = chars.AsSpan(0, count);
                if (first && !span.IsEmpty)
                {
                    first = false;
                    if (span[0] == '\uFEFF') span = span[1..];
                }
                if (span.IsEmpty) return;
                InterceptOutputToUITerminal?.Invoke(ref span);
                if (span.IsEmpty) return;
                var text = span.ToString();
                WriteToUITerminal(text);
                ConsoleOutputLog?.Append(text);
            }

            while (true)
            {
                var closed = consoleClosed.Task.IsCompleted;
                if (!PeekNamedPipe(stream.SafeFileHandle, IntPtr.Zero, 0, IntPtr.Zero, out var available, IntPtr.Zero))
                {
                    var error = Marshal.GetLastWin32Error();
                    if (error == 109) break; // ERROR_BROKEN_PIPE
                    throw new Win32Exception(error, "Could not inspect terminal output.");
                }

                if (available != 0)
                {
                    var read = stream.Read(bytes, 0, (int)Math.Min(available, (uint)bytes.Length));
                    if (read == 0) break;
                    Publish(decoder.GetChars(bytes, 0, read, chars, 0, flush: false));
                    continue;
                }
                if (closed)
                {
                    consoleClosed.Task.GetAwaiter().GetResult();
                    break;
                }
                Thread.Sleep(10); // Polling cadence, not a deadline or completion condition.
            }
            Publish(decoder.GetChars([], 0, 0, chars, 0, flush: true));
        }
        catch (Exception exception)
        {
            readerError = ExceptionDispatchInfo.Capture(exception);
            throw;
        }
        finally { readerFinished.Set(); }
    }

    private sealed class ClosingFactory(DrainingTerminal terminal, IProcessFactory inner) : IProcessFactory
    {
        public IProcess Start(string command, nuint attributes, PseudoConsole console, string? workingDirectory = null)
        {
            var process = inner.Start(command, attributes, console, workingDirectory);
            _ = Task.Run(() =>
            {
                try
                {
                    process.WaitForExit();
                    // Keep the reader active while ClosePseudoConsole flushes output.
                    console.Dispose();
                    terminal.consoleClosed.TrySetResult();
                }
                catch (Exception exception) { terminal.consoleClosed.TrySetException(exception); }
            });
            return process;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool PeekNamedPipe(SafeFileHandle pipe, IntPtr buffer, uint bufferSize,
        IntPtr bytesRead, out uint totalBytesAvailable, IntPtr bytesLeftThisMessage);
}
