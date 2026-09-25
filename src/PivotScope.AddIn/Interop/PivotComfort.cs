using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>A cube field, as it appears in the field list.</summary>
public sealed record FieldVisibility(
    string Name, string Caption, bool ShownInFieldList, string Area);

/// <summary>A level of a hierarchy, and whether it is shown in the table.</summary>
public sealed record LevelVisibility(string Name, string Caption, bool Shown);

/// <summary>
/// PivotTable building conveniences.
///
/// Not to be confused with Excel's native "Show/hide fields" menu, which
/// toggles the MEMBER PROPERTIES of a given field. Here we hide whole
/// fields from the FIELD LIST, which is the only way to make a cube that
/// exposes hundreds of them usable.
///
/// Call exclusively through <see cref="ExcelThread"/>.
/// </summary>
public static class PivotComfort
{
    private static Xl.PivotTable RequirePivot()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        Xl.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; } catch { /* outside a PivotTable */ }

        return pivot ?? throw new InvalidOperationException(
            "Placez le curseur dans un tableau croisé dynamique.");
    }

    public static IReadOnlyList<FieldVisibility> ListFields()
    {
        var pivot = RequirePivot();
        var fields = new List<FieldVisibility>();

        foreach (Xl.CubeField cf in pivot.CubeFields)
        {
            var area = cf.Orientation switch
            {
                Xl.XlPivotFieldOrientation.xlRowField => "row",
                Xl.XlPivotFieldOrientation.xlColumnField => "column",
                Xl.XlPivotFieldOrientation.xlPageField => "filter",
                Xl.XlPivotFieldOrientation.xlDataField => "data",
                _ => "",
            };

            bool shown;
            try { shown = cf.ShowInFieldList; } catch { shown = true; }

            fields.Add(new FieldVisibility(cf.Name, cf.Caption, shown, area));
        }

        return fields;
    }

    public static void SetFieldVisibility(string cubeFieldName, bool visible)
    {
        var pivot = RequirePivot();

        foreach (Xl.CubeField cf in pivot.CubeFields)
        {
            if (!string.Equals(cf.Name, cubeFieldName, StringComparison.Ordinal)) continue;

            // Hiding a field placed on the PivotTable would remove it from view
            // without the user asking for it: refuse rather than surprise.
            if (!visible && cf.Orientation != Xl.XlPivotFieldOrientation.xlHidden)
                throw new InvalidOperationException(
                    $"« {cf.Caption} » est utilisé dans le tableau croisé dynamique. " +
                    "Retirez-le de la disposition avant de le masquer de la liste.");

            cf.ShowInFieldList = visible;
            return;
        }

        throw new InvalidOperationException(
            $"Champ introuvable dans le tableau croisé dynamique : {cubeFieldName}");
    }

    /// <summary>
    /// The levels of a hierarchy placed on the table. A CubeField exposes
    /// one PivotField per level; their Hidden property decides whether
    /// they are shown.
    /// </summary>
    public static IReadOnlyList<LevelVisibility> ListLevels(string cubeFieldName)
    {
        var field = FindCubeField(RequirePivot(), cubeFieldName);
        var levels = new List<LevelVisibility>();

        foreach (Xl.PivotField pf in field.PivotFields)
        {
            // CubeField.PivotFields mixes LEVELS and MEMBER
            // PROPERTIES. On a real hierarchy, three levels can end up
            // buried among forty properties: IsMemberProperty does the
            // sorting, and without it the function is unusable.
            if (IsMemberProperty(pf)) continue;

            bool hidden;
            try { hidden = pf.Hidden; } catch { hidden = false; }
            levels.Add(new LevelVisibility(pf.Name, SafeCaption(pf), !hidden));
        }

        return levels;
    }

    /// <summary>Milliseconds elapsed since the marker, which it resets.</summary>
    private static long Since(ref long timestamp)
    {
        var ms = (long)System.Diagnostics.Stopwatch.GetElapsedTime(timestamp).TotalMilliseconds;
        timestamp = System.Diagnostics.Stopwatch.GetTimestamp();
        return ms;
    }

    private static void TryDrillDown(Xl.PivotField field)
    {
        try
        {
            if (!field.DrilledDown) field.DrilledDown = true;
        }
        catch (Exception ex)
        {
            FileLog.Write($"Level '{field.Name}': drill-down refused.", ex);
        }
    }

    private static bool IsMemberProperty(Xl.PivotField field)
    {
        try { return field.IsMemberProperty; } catch { return false; }
    }

    /// <summary>
    /// Applies the level selection.
    ///
    /// **Two passes, and the order is not cosmetic**: Excel refuses to
    /// hide the last visible level of a hierarchy. So we first show what
    /// must be shown, and only then hide.
    /// </summary>
    public static IReadOnlyList<LevelVisibility> SetLevelVisibility(
        string cubeFieldName, IReadOnlyList<string> shownLevelNames)
    {
        if (shownLevelNames.Count == 0)
            throw new InvalidOperationException(
                "Gardez au moins un niveau affiché : une hiérarchie sans niveau " +
                "visible n'a plus de sens dans le tableau.");

        var app = (Xl.Application)ExcelDnaUtil.Application;
        var pivot = RequirePivot();
        var field = FindCubeField(pivot, cubeFieldName);
        var wanted = new HashSet<string>(shownLevelNames, StringComparer.Ordinal);

        // Instrumentation. Be careful about what it really measures:
        // PivotTable.MDX describes the query of the LAST refresh performed,
        // and throws in several documented cases. A comparison alone therefore
        // does not tell "query unchanged" from "query unreadable" nor
        // from "refresh not done yet". We log the read state and the
        // duration, without which the comparison is worthless.
        var mdxBefore = ReadMdx(pivot);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        // Without this wrapper, EACH toggle triggers a rebuild of the
        // table and a server round trip: hiding a level on a heavy
        // hierarchy then takes several seconds. Deferring the layout
        // brings it all down to a single rebuild.
        var previousManual = false;
        try { previousManual = pivot.ManualUpdate; } catch { /* not readable */ }

        app.ScreenUpdating = false;
        try { pivot.ManualUpdate = true; } catch { /* not settable */ }

        // Per-step timing: without it, all we know is that "it is
        // slow", not which of the three operations is costly. Measured on a real
        // cube, expanding the hidden levels is the main suspect —
        // expanding a hierarchy of several thousand members can request
        // the whole tree from the server.
        long showMs = 0, drillMs = 0, hideMs = 0, rebuildMs = 0;

        try
        {
            // First pass: show. This guarantees that at least one
            // level stays visible before any is hidden.
            var levels = new List<Xl.PivotField>();
            foreach (Xl.PivotField pf in field.PivotFields)
                if (!IsMemberProperty(pf)) levels.Add(pf);

            var step = System.Diagnostics.Stopwatch.GetTimestamp();
            var firstVisible = -1;
            for (var i = 0; i < levels.Count; i++)
            {
                if (!wanted.Contains(levels[i].Name)) continue;
                TrySetHidden(levels[i], false);
                if (firstVisible < 0) firstVisible = i;
            }
            showMs = Since(ref step);

            // Hidden levels ABOVE the first visible one must be
            // drilled down, otherwise the table stays collapsed on them and the
            // level we wanted to see never appears. The last level is
            // never drilled down: there is nothing below it.
            for (var i = 0; i < firstVisible && i < levels.Count - 1; i++)
                TryDrillDown(levels[i]);
            drillMs = Since(ref step);

            // Second pass: hide.
            foreach (var pf in levels)
                if (!wanted.Contains(pf.Name)) TrySetHidden(pf, true);
            hideMs = Since(ref step);
        }
        finally
        {
            // The actual rebuild happens HERE, when control goes back to
            // Excel: everything before only stacks up pending
            // changes. So this very line carries the real cost.
            var rebuild = System.Diagnostics.Stopwatch.GetTimestamp();
            try { pivot.ManualUpdate = previousManual; } catch { /* not settable */ }
            rebuildMs = Since(ref rebuild);

            app.ScreenUpdating = true;
        }

        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        var mdxAfter = ReadMdx(pivot);

        var verdict = (mdxBefore.Readable, mdxAfter.Readable) switch
        {
            (false, _) or (_, false) =>
                "MDX query UNREADABLE — the comparison is meaningless",
            _ when mdxBefore.Text == mdxAfter.Text => "MDX query unchanged",
            _ => "MDX query changed",
        };

        FileLog.Write(
            $"Levels applied in {elapsed:F0} ms — {verdict}. " +
            $"Breakdown: show {showMs} ms, drill down {drillMs} ms, " +
            $"hide {hideMs} ms, rebuild {rebuildMs} ms.");

        if (mdxBefore.Readable && mdxAfter.Readable && mdxBefore.Text != mdxAfter.Text)
            FileLog.Write($"  before: {mdxBefore.Text}\n  after: {mdxAfter.Text}");

        return ListLevels(cubeFieldName);
    }

    /// <summary>
    /// Instrumented read of PivotTable.MDX: explicitly tells "unreadable"
    /// apart from "empty", otherwise a comparison between two failures
    /// would read as equality.
    /// </summary>
    private readonly record struct MdxReading(bool Readable, string Text)
    {
        public string Describe() => Readable ? $"{Text.Length} chars" : "unreadable";
    }

    private static MdxReading ReadMdx(Xl.PivotTable pivot)
    {
        try { return new MdxReading(true, pivot.MDX ?? string.Empty); }
        catch (Exception ex)
        {
            FileLog.Write("PivotTable.MDX unreadable.", ex);
            return new MdxReading(false, string.Empty);
        }
    }

    /// <summary>
    /// Applies pending changes and queries the server once.
    /// Essential when the layout is deferred: otherwise the user
    /// stacks up actions without ever seeing the result.
    /// </summary>
    public static void RefreshNow()
    {
        var pivot = RequirePivot();
        var cache = pivot.PivotCache();

        // Refresh disabled on the cache is the workbook author's choice (a
        // frozen snapshot): turning it back on silently would undo it.
        if (!cache.EnableRefresh)
            throw new InvalidOperationException(
                "L'actualisation de ce tableau croisé dynamique est désactivée dans le " +
                "classeur (Options du tableau croisé dynamique > Données). Réactivez-la " +
                "si vous voulez l'actualiser.");
        try { pivot.ManualUpdate = false; } catch { /* not settable */ }

        pivot.RefreshTable();
    }

    /// <summary>
    /// Deferred layout: several fields are dropped, nothing is sent to the
    /// server, then <see cref="RefreshNow"/> applies everything at once.
    ///
    /// Not to be confused with PivotCache.EnableRefresh, which FORBIDS
    /// refreshing — Excel's button included — and leaves the user with no
    /// way to see their table.
    /// </summary>
    public static bool SetDeferLayout(bool deferred)
    {
        var pivot = RequirePivot();
        pivot.ManualUpdate = deferred;
        if (!deferred) pivot.RefreshTable();
        return deferred;
    }

    private static void TrySetHidden(Xl.PivotField field, bool hidden)
    {
        try
        {
            if (field.Hidden != hidden) field.Hidden = hidden;
        }
        catch (Exception ex)
        {
            // Excel may refuse a specific level; log it and
            // carry on, rather than abandon the whole selection.
            FileLog.Write($"Level '{field.Name}': toggle refused by Excel.", ex);
        }
    }

    private static Xl.CubeField FindCubeField(Xl.PivotTable pivot, string name)
    {
        foreach (Xl.CubeField cf in pivot.CubeFields)
            if (string.Equals(cf.Name, name, StringComparison.Ordinal))
                return cf;

        throw new InvalidOperationException(
            $"Champ introuvable dans le tableau croisé dynamique : {name}");
    }

    private static string SafeCaption(Xl.PivotField field)
    {
        try { return field.Caption; } catch { return field.Name; }
    }

    public static int ShowAllFields()
    {
        var pivot = RequirePivot();
        var restored = 0;

        foreach (Xl.CubeField cf in pivot.CubeFields)
        {
            try
            {
                if (cf.ShowInFieldList) continue;
                cf.ShowInFieldList = true;
                restored++;
            }
            catch { /* stubborn field: carry on, the count will tell */ }
        }

        return restored;
    }

    /// <summary>
    /// Is the layout deferred? Outside a PivotTable the answer is "no", which is
    /// Excel's default state.
    /// </summary>
    public static bool IsLayoutDeferred()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        Xl.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; } catch { /* outside a PivotTable */ }
        if (pivot is null) return false;

        try { return pivot.ManualUpdate; } catch { return false; }
    }
}
