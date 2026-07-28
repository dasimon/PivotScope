namespace PivotScope.Core.Calculations;

/// <summary>
/// Vérifie une définition de calcul avant de la présenter à Excel.
///
/// Le but n'est pas de valider le MDX — seul le serveur en est juge — mais
/// d'attraper ce qui produirait une erreur COM opaque ou, pire, un réglage
/// silencieusement ignoré.
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

        // Les deux règles suivantes sont documentées par Microsoft. Sans elles,
        // le réglage est accepté puis ignoré — le pire des comportements.
        if (definition.NumberFormat is { Length: > 0 } &&
            definition.Kind is not CalculationKind.Member)
            messages.Add("Le format de nombre n'est valide que pour un membre calculé.");

        if (definition.DisplayFolder is { Length: > 0 } &&
            definition.Kind is not CalculationKind.Measure)
            messages.Add("Le dossier d'affichage n'est valide que pour une mesure calculée.");

        return messages;
    }

    /// <summary>Nom unique MDX du calcul, tel qu'Excel devra le connaître.</summary>
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
