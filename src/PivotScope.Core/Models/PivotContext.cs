namespace PivotScope.Core.Models;

/// <summary>A PivotTable field. Area ∈ row | column | filter | data.</summary>
public sealed record PivotFieldInfo(string Caption, string UniqueName, string Area);

/// <summary>
/// Snapshot of the active PivotTable, with no Excel type at all: that is what
/// makes it serializable to the SPA and testable outside Excel.
/// When HasPivot or IsOlap is false, Diagnostic carries the message to display —
/// having no PivotTable is not an error, it is a normal state of the task pane.
/// </summary>
/// <param name="PivotKey">
/// Workbook + sheet + name of the PivotTable. The name alone designates
/// nothing: Excel calls the first PivotTable of every sheet "PivotTable1".
/// The pane compares keys to know when to drop what it shows.
/// </param>
/// <param name="DiagnosticCode">
/// Stable code of <paramref name="Diagnostic"/> (noPivot, notOlap, powerPivot,
/// connectionUnreadable): the pane translates it, the French text is the fallback.
/// </param>
public sealed record PivotContext(
    bool HasPivot,
    bool IsOlap,
    string? Server,
    string? Catalog,
    string? Cube,
    string? Mdx,
    IReadOnlyList<PivotFieldInfo> Fields,
    string? Diagnostic,
    string? PivotKey = null,
    string? DiagnosticCode = null)
{
    public static PivotContext None(string diagnostic, string? code = null) =>
        new(false, false, null, null, null, null, [], diagnostic, null, code);
}
