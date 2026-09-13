using CubeScope.Core.Models;
using CubeScope.Core.Script;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Provenance;

/// <summary>
/// Answers "where does this number come from?".
///
/// Starts from the tuple returned by Excel, finds the measure in the cube's MDX
/// Script and walks up its dependencies. Never throws: missing information
/// becomes a <see cref="CellProvenance.Note"/>, because showing the tuple
/// alone is better than showing nothing.
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
            // The graph is a convenience: its failure must not cost us the expression.
        }

        return new CellProvenance(
            tuple, parsed.Measure, parsed.Coordinates,
            command.Expression, command.StartLine, dependencies, null);
    }

    /// <summary>
    /// Finds the script command. Depending on the cube, a calculated measure can
    /// be named with or without the <c>[Measures].</c> prefix: both forms are
    /// compared rather than betting on one.
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
