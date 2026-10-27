using System;
using System.IO;

namespace F1Controller.Engineer;

/// <summary>
/// One line per engineer call (time, priority, key, text) or engineer error, for reviewing a
/// race afterwards. Safe to call from any thread.
/// </summary>
public sealed class CallLog
{
    private readonly TextWriter _writer;
    private readonly Func<DateTime> _now;
    private readonly object _lock = new();

    public CallLog(TextWriter writer, Func<DateTime>? now = null)
    {
        _writer = writer;
        _now = now ?? (() => DateTime.Now);
    }

    public void Write(RadioCall call) =>
        WriteLine($"P{(int)call.Priority}  {call.Key}  {call.Text}");

    public void WriteError(Exception ex) => WriteLine($"ERROR  {ex}");

    private void WriteLine(string text)
    {
        lock (_lock)
        {
            _writer.WriteLine($"{_now():HH:mm:ss.fff}  {text}");
            _writer.Flush();
        }
    }
}
