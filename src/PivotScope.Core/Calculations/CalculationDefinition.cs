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
