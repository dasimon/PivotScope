using CubeScope.Core.Models;
using CubeScope.Core.Script;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Provenance;

/// <summary>
/// Answers "where does this number come from?".
///
/// Starts from the tuple returned by Excel, finds the measure in the cube's MDX
/// Script and walks up its dependencies. Never throws (except on cancellation):
/// missing information becomes a <see cref="CellProvenance.Note"/>, because
/// showing the tuple alone is better than showing nothing.
///
/// A measure is only called "physical" with the evidence that would say
/// otherwise ruled out: a SCOPE assignment on the measure, or a calculated
/// member among the coordinates (a time-utility dimension, typically), both
/// change the figure without any CREATE MEMBER for the measure.
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
        try
        {
            script = await scripts.GetScriptAsync(cube, ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new CellProvenance(
                tuple, parsed.Measure, parsed.Coordinates, null, null, null,
                $"Le script du cube n'a pas pu être lu : {ex.Message}");
        }

        var command = FindCalculatedMember(script, parsed.Measure);
        if (command is null)
            return new CellProvenance(
                tuple, parsed.Measure, parsed.Coordinates, null, null, null,
                PhysicalMeasureNote(script, parsed));

        DependencyGraph? dependencies = null;
        try
        {
            var meta = await metadata.GetCubeMetaAsync(cube, ct).ConfigureAwait(false);
            dependencies = DependencyService.Resolve(script, meta, command.Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The graph is a convenience: its failure must not cost us the expression.
        }

        return new CellProvenance(
            tuple, parsed.Measure, parsed.Coordinates,
            command.Expression, command.StartLine, dependencies,
            CalculatedCoordinatesNote(script, parsed));
    }

    /// <summary>
    /// No CREATE MEMBER for the measure. Say so, but name what else in the
    /// script may still change this figure rather than asserting "physical".
    /// </summary>
    private static string PhysicalMeasureNote(CubeScript script, MdxTuple parsed)
    {
        var scopes = script.Commands
            .Where(c => c.Kind == "Scope" && ReferencesMeasure(c.Expression, parsed.Measure!))
            .Select(c => $"ligne {c.StartLine}")
            .ToList();

        var note = "Aucune définition CREATE MEMBER pour cette mesure : a priori une mesure " +
                   "physique, qui vient directement du cube.";

        if (scopes.Count > 0)
            note += $" Attention : {scopes.Count} affectation(s) SCOPE du script la " +
                    $"mentionnent ({string.Join(", ", scopes)}) et peuvent en modifier la valeur.";

        return note + (CalculatedCoordinatesNote(script, parsed) is { } extra ? " " + extra : string.Empty);
    }

    /// <summary>A calculated member among the coordinates changes the figure too.</summary>
    private static string? CalculatedCoordinatesNote(CubeScript script, MdxTuple parsed)
    {
        var calculated = parsed.Coordinates
            .Where(coordinate => script.Commands.Any(c =>
                c.Kind == "CalculatedMember" && Same(c.Name, coordinate)))
            .ToList();

        return calculated.Count == 0
            ? null
            : $"Coordonnée(s) calculée(s) par le script : {string.Join(", ", calculated)}.";
    }

    private static bool ReferencesMeasure(string text, string measure)
    {
        var bare = Bare(measure);
        return text.Contains(measure, StringComparison.OrdinalIgnoreCase)
            || text.Contains($"Measures.[{bare}]", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Finds the CREATE MEMBER of the measure. Depending on the cube, a
    /// calculated measure can be named with or without the <c>[Measures].</c>
    /// prefix: both forms are compared rather than betting on one. Only
    /// calculated members count — a named set or a SCOPE sharing the name is
    /// not the definition of the measure.
    /// </summary>
    private static ScriptCommand? FindCalculatedMember(CubeScript script, string measure)
    {
        var bare = $"[{Bare(measure)}]";

        foreach (var command in script.Commands)
        {
            if (command.Kind != "CalculatedMember") continue;
            if (Same(command.Name, measure) || Same(command.Name, bare))
                return command;
        }

        return null;
    }

    private static string Bare(string measure)
    {
        var name = measure.StartsWith(MeasuresPrefix, StringComparison.OrdinalIgnoreCase)
            ? measure[MeasuresPrefix.Length..]
            : measure;
        return name.Trim().TrimStart('[').TrimEnd(']');
    }

    private static bool Same(string a, string b)
        => string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
