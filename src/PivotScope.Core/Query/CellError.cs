namespace PivotScope.Core.Query;

/// <summary>
/// A cell the server returned in error. Kept as its own type rather than as
/// text: written to Excel it becomes a real error value (#VALUE!), which a
/// SUM propagates instead of silently skipping, and it can never be mistaken
/// for a member caption.
/// </summary>
public sealed record CellError(string Message)
{
    /// <summary>
    /// What CubeScope's CellSetMapper writes in place of a cell in error. Any
    /// code reading a formatted QueryResult must treat it as an error, not as text.
    /// </summary>
    public const string LegacyMarker = "#ERREUR";
}
