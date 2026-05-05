namespace JmcLogConsole.ViewModels;

public sealed record LogViewportSnapshot(
    IReadOnlyList<LogRenderLine> Lines,
    int TotalCount,
    int MatchCount,
    int TotalRows,
    int FirstRow,
    int LastRow,
    bool FollowTail,
    string? FilterError)
{
    public static LogViewportSnapshot Empty(bool followTail = true, string? filterError = null)
    {
        return new LogViewportSnapshot([], 0, 0, 0, 0, 0, followTail, filterError);
    }
}
