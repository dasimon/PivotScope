namespace PivotScope.Core.Models;

/// <summary>A PivotTable field. Area ∈ row | column | filter | data.</summary>
public sealed record PivotFieldInfo(string Caption, string UniqueName, string Area);

/// <summary>
/// Snapshot of the active PivotTable, with no Excel type at all: that is what
/// makes it serializable to the SPA and testable outside Excel.
/// When HasPivot or IsOlap is false, Diagnostic carries the message to display —
/// having no PivotTable is not an error, it is a normal state of the task pane.
/// </summary>
public sealed record PivotContext(
    bool HasPivot,
    bool IsOlap,
    string? Server,
    string? Catalog,
    string? Cube,
    string? Mdx,
    IReadOnlyList<PivotFieldInfo> Fields,
    string? Diagnostic)
{
    public static PivotContext None(string diagnostic) =>
        new(false, false, null, null, null, null, [], diagnostic);
}
