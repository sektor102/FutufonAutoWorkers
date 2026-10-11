using System;

namespace FutufonAutoWorkerMscModLoader
{
    internal enum LogKind { Event, Progress, Detail, Snapshot }

    // Console output is for events. File output requires explicit opt-in.
    internal sealed class SessionLogger
    {
        private readonly Action<string> _file, _console;
        internal bool FileEnabled { get; set; }
        internal bool Detailed { get; set; }
        internal SessionLogger(Action<string> file, Action<string> console) { _file = file; _console = console; }
        internal void Write(string message, LogKind kind)
        {
            if (kind == LogKind.Event) _console(message);
            if (!FileEnabled || (kind == LogKind.Detail && !Detailed)) return;
            _file(message);
        }
        internal static bool ShouldSample(string owner, string fsmName, bool fullSnapshot)
        {
            // Minute floats change every frame. Read them for reminders, dump them only on F9/errors.
            return fullSnapshot || owner != "FACTORY" || fsmName != "Clock";
        }
    }
}
