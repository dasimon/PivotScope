using CubeScope.Core.Models;
using CubeScope.Core.Script;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Provenance;

/// <summary>
/// Répond à « d'où vient ce chiffre ? ».
///
/// Part du tuple rendu par Excel, retrouve la mesure dans le MDX Script du cube
/// et remonte ses dépendances. Ne lève jamais : une information manquante
/// devient une <see cref="CellProvenance.Note"/>, parce qu'afficher le tuple
/// seul vaut mieux que n'afficher rien.
/// </summary>
public sealed class ProvenanceService(IScriptReader scripts, ICubeMetadataReader metadata)
{
    private const string MeasuresPrefix = "[Measures].";

    public async Task<CellProvenance> DescribeAsync(
        string cube, string tuple, CancellationToken ct = default)
    {
        var parsed = TupleParser.Parse(tuple);

        if (parsed.Measure is null)
            return new CellProvenance(
                tuple, null, parsed.Coordinates, null, null, null,
                "Aucune mesure dans les coordonnées de cette cellule.");

        CubeScript script;
        CubeMeta meta;
        try
        {
            script = await scripts.GetScriptAsync(cube, ct).ConfigureAwait(false);
            meta = await metadata.GetCubeMetaAsync(cube, ct).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            return new CellProvenance(
                tuple, parsed.Measure, parsed.Coordinates, null, null, null,
                $"Le script du cube n'a pas pu être lu : {ex.Message}");
        }

        var command = FindCommand(script, parsed.Measure);
        if (command is null)
            return new CellProvenance(
                tuple, parsed.Measure, parsed.Coordinates, null, null, null,
                "Cette mesure est physique : elle vient directement du cube et " +
                "n'a pas d'expression MDX.");

        DependencyGraph? dependencies = null;
        try
        {
            dependencies = DependencyService.Resolve(script, meta, command.Name);
        }
        catch
        {
            // Le graphe est un confort : son échec ne doit pas priver de l'expression.
        }

        return new CellProvenance(
            tuple, parsed.Measure, parsed.Coordinates,
            command.Expression, command.StartLine, dependencies, null);
    }

    /// <summary>
    /// Retrouve la commande du script. Selon les cubes, une mesure calculée peut
    /// être nommée avec ou sans le préfixe <c>[Measures].</c> : on compare les
    /// deux formes plutôt que de parier sur l'une.
    /// </summary>
    private static ScriptCommand? FindCommand(CubeScript script, string measure)
    {
        var bare = measure.StartsWith(MeasuresPrefix, StringComparison.OrdinalIgnoreCase)
            ? measure[MeasuresPrefix.Length..]
            : measure;

        foreach (var command in script.Commands)
        {
            if (Same(command.Name, measure) || Same(command.Name, bare))
                return command;
        }

        return null;
    }

    private static bool Same(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
