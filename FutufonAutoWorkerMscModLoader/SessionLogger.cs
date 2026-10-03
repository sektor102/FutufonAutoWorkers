using System;

namespace FutufonAutoWorkerMscModLoader
{
    internal enum LogKind { Event, Progress, Detail, Snapshot }

    // Console output is for events. Progress/snapshots stay in the session file.
    internal sealed class SessionLogger
    {
        private readonly Action<string> _file, _console;
        internal bool Detailed { get; set; }
        internal SessionLogger(Action<string> file, Action<string> console) { _file = file; _console = console; }
        internal void Write(string message, LogKind kind)
        {
            if (kind == LogKind.Detail && !Detailed) return;
            _file(message);
            if (kind == LogKind.Event) _console(message);
        }
        internal static bool ShouldSample(string owner, string fsmName, bool fullSnapshot)
        {
            // Minute floats change every frame. Read them for reminders, dump them only on F9/errors.
            return fullSnapshot || owner != "FACTORY" || fsmName != "Clock";
        }
    }
}
