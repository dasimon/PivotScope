namespace PivotScope.Core.Calculations;

/// <summary>
/// Les trois natures de calcul qu'un TCD OLAP accepte. Les valeurs suivent
/// XlCalculatedMemberType côté Excel : Member=0, Set=1, Measure=2.
/// </summary>
public enum CalculationKind
{
    Member = 0,
    Set = 1,
    Measure = 2,
}

/// <summary>
/// Un calcul tel que l'utilisateur le définit.
///
/// <para><see cref="NumberFormat"/> mérite un mot : la documentation Excel
/// précise que ce réglage « can only be set by macros. There is no user
/// interface for setting them ». PivotScope sait donc formater un membre
/// calculé, ce qu'Excel ne permet pas par son interface.</para>
/// </summary>
public sealed record CalculationDefinition(
    string Name,
    string Expression,
    CalculationKind Kind,
    string? DisplayFolder = null,
    string? NumberFormat = null,
    string? ParentHierarchy = null,
    int SolveOrder = 0);
