namespace PivotScope.Core.Calculations;

/// <summary>
/// The three kinds of calculation an OLAP PivotTable accepts. The values follow
/// XlCalculatedMemberType on the Excel side: Member=0, Set=1, Measure=2.
/// </summary>
public enum CalculationKind
{
    Member = 0,
    Set = 1,
    Measure = 2,
}

/// <summary>
/// The number format Excel lets a macro set on a calculated member. It is NOT
/// a format string: AddCalculatedMember's NumberFormat takes the
/// XlCalcMemNumberFormatType enumeration — Default = 0, Number = 1,
/// Percent = 2 (learn.microsoft.com/office/vba/api/excel.xlcalcmemnumberformattype).
/// </summary>
public static class CalculationNumberFormat
{
    public const string Number = "number";
    public const string Percent = "percent";

    /// <summary>
    /// Excel's enumeration value, or null for the default. Free text saved by
    /// earlier releases ("0.00%", "#,##0.00") is read by intent: a "%" means
    /// a percentage, any other non-empty text a number.
    /// </summary>
    public static int? ToExcel(string? format)
    {
        if (string.IsNullOrWhiteSpace(format)) return null;
        var value = format.Trim();
        if (value.Equals(Percent, StringComparison.OrdinalIgnoreCase) || value.Contains('%')) return 2;
        if (value.Equals("default", StringComparison.OrdinalIgnoreCase)) return null;
        return 1;
    }

    /// <summary>Excel's enumeration value back to the stored keyword.</summary>
    public static string? FromExcel(int value) => value switch
    {
        1 => Number,
        2 => Percent,
        _ => null,
    };
}

/// <summary>
/// A calculation as the user defines it.
///
/// <para><see cref="NumberFormat"/> deserves a word: the Excel documentation
/// states that this setting "can only be set by macros. There is no user
/// interface for setting them". PivotScope can therefore format a calculated
/// member, which Excel does not allow through its UI.</para>
/// </summary>
public sealed record CalculationDefinition(
    string Name,
    string Expression,
    CalculationKind Kind,
    string? DisplayFolder = null,
    string? NumberFormat = null,
    string? ParentHierarchy = null,
    int SolveOrder = 0);
