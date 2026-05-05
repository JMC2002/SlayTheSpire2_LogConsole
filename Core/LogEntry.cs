using MegaCrit.Sts2.Core.Logging;

namespace JmcLogConsole.Core;

public readonly record struct LogEntry(
    long Sequence,
    DateTime Time,
    LogLevel Level,
    string Message);
