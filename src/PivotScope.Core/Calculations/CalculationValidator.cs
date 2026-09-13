namespace PivotScope.Core.Calculations;

/// <summary>
/// Checks a calculation definition before handing it to Excel.
///
/// The goal is not to validate the MDX — only the server can judge that — but
/// to catch what would produce an opaque COM error or, worse, a setting that
/// is silently ignored.
/// </summary>
public static class CalculationValidator
{
    public static IReadOnlyList<string> Validate(CalculationDefinition definition)
    {
        var messages = new List<string>();

        if (string.IsNullOrWhiteSpace(definition.Name))
            messages.Add("Le nom du calcul est obligatoire.");
        else if (definition.Name.Contains('[') || definition.Name.Contains(']'))
            messages.Add("Le nom ne peut pas contenir de crochets : ils délimitent " +
                         "les identifiants MDX.");

        if (string.IsNullOrWhiteSpace(definition.Expression))
            messages.Add("L'expression MDX est obligatoire.");

        if (definition.Kind is CalculationKind.Member &&
            string.IsNullOrWhiteSpace(definition.ParentHierarchy))
            messages.Add("Un membre calculé doit indiquer sa hiérarchie parente.");

        // The next two rules are documented by Microsoft. Without them, the
        // setting is accepted and then ignored — the worst possible behaviour.
        if (definition.NumberFormat is { Length: > 0 } &&
            definition.Kind is not CalculationKind.Member)
            messages.Add("Le format de nombre n'est valide que pour un membre calculé.");

        if (definition.DisplayFolder is { Length: > 0 } &&
            definition.Kind is not CalculationKind.Measure)
            messages.Add("Le dossier d'affichage n'est valide que pour une mesure calculée.");

        return messages;
    }

    /// <summary>MDX unique name of the calculation, as Excel will need to know it.</summary>
    public static string QualifiedName(CalculationDefinition definition)
    {
        var name = definition.Name.Trim();
        return definition.Kind switch
        {
            CalculationKind.Measure => $"[Measures].[{name}]",
            _ => $"{definition.ParentHierarchy}.[{name}]",
        };
    }
}
