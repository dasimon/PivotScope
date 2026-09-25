using System.Text;
using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using PivotScope.Core.Calculations;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>A calculation already present on the PivotTable.</summary>
public sealed record ExistingCalculation(
    string Name, string Formula, string Kind, bool IsValid, string? DisplayFolder);

/// <summary>
/// Creates, lists and deletes the calculations of an OLAP PivotTable.
///
/// Documented points that must be respected, or you get opaque errors or
/// settings silently ignored:
/// — since Excel 2013, measures and members go through AddCalculatedMember;
///   only named SETS still use Add, followed by CubeFields.AddSet;
/// — DisplayFolder is only valid for a measure, NumberFormat only for a
///   member (validated upstream by CalculationValidator);
/// — IsValid returns True when the PivotTable is not connected: you must call
///   PivotCache.MakeConnection() before relying on it, otherwise a broken
///   calculation is reported as valid.
///
/// Call exclusively through <see cref="ExcelThread"/>.
/// </summary>
public static class CalculationApplier
{
    private static Xl.PivotTable RequirePivot()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        Xl.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; } catch { /* outside a PivotTable */ }

        var found = pivot ?? throw new InvalidOperationException(
            "Placez le curseur dans un tableau croisé dynamique.");

        if (!found.PivotCache().OLAP)
            throw new InvalidOperationException(
                "Les calculs MDX ne s'appliquent qu'à un tableau croisé dynamique OLAP.");

        return found;
    }

    public static IReadOnlyList<ExistingCalculation> List()
    {
        var pivot = RequirePivot();
        EnsureConnected(pivot);

        var list = new List<ExistingCalculation>();
        foreach (Xl.CalculatedMember member in pivot.CalculatedMembers)
        {
            string? folder = null;
            try { folder = member.DisplayFolder; } catch { /* not a measure */ }

            bool valid;
            try { valid = member.IsValid; } catch { valid = false; }

            list.Add(new ExistingCalculation(
                member.Name,
                SafeFormula(member),
                KindLabel(member),
                valid,
                folder));
        }

        return list;
    }

    public static string Apply(CalculationDefinition definition, bool addToPivot)
    {
        var messages = CalculationValidator.Validate(definition);
        if (messages.Count > 0)
            throw new InvalidOperationException(string.Join(" ", messages));

        var pivot = RequirePivot();
        EnsureConnected(pivot);

        var uniqueName = CalculationValidator.QualifiedName(definition);

        // Replace rather than fail on a duplicate: that is the expected action
        // while fine-tuning an expression. But the working version is kept
        // until the new one has been accepted: a typo must not cost the user
        // the calculation they had, nor its place in the values area.
        var previous = Capture(pivot, uniqueName);
        if (previous is not null) previous.Member.Delete();

        Xl.CalculatedMember created;
        try
        {
            created = Create(pivot, uniqueName, definition.Name.Trim(), definition.Expression,
                definition.SolveOrder, definition.Kind, definition.DisplayFolder,
                definition.NumberFormat, definition.ParentHierarchy);

            if (!created.IsValid)
            {
                created.Delete();
                throw new InvalidOperationException(
                    "Le serveur a refusé ce calcul : vérifiez l'expression MDX." +
                    (previous is null ? string.Empty : " La version précédente a été conservée."));
            }
        }
        catch
        {
            if (previous is not null) Recreate(pivot, uniqueName, previous);
            throw;
        }

        // A measure that was shown stays shown: deleting it to recreate it
        // took it out of the values area, whatever the checkbox says.
        if (definition.Kind is CalculationKind.Measure && (addToPivot || previous?.DataPosition is not null))
        {
            ShowMeasure(pivot, uniqueName, definition.Name.Trim());
            if (previous?.DataPosition is int position) TryRestorePosition(pivot, uniqueName, position);
        }

        return uniqueName;
    }

    /// <summary>What it takes to put a calculation back exactly as it was.</summary>
    private sealed record PreviousCalculation(
        Xl.CalculatedMember Member,
        string Formula,
        int SolveOrder,
        CalculationKind Kind,
        string? DisplayFolder,
        string? NumberFormat,
        string? ParentHierarchy,
        string Caption,
        int? DataPosition);

    private static PreviousCalculation? Capture(Xl.PivotTable pivot, string uniqueName)
    {
        foreach (Xl.CalculatedMember member in pivot.CalculatedMembers)
        {
            if (!string.Equals(member.Name, uniqueName, StringComparison.OrdinalIgnoreCase))
                continue;

            var kind = member.Type switch
            {
                Xl.XlCalculatedMemberType.xlCalculatedMeasure => CalculationKind.Measure,
                Xl.XlCalculatedMemberType.xlCalculatedSet => CalculationKind.Set,
                _ => CalculationKind.Member,
            };

            string? folder = null, format = null, parent = null;
            try { folder = Blank(member.DisplayFolder); } catch { /* not a measure */ }
            try { format = CalculationNumberFormat.FromExcel((int)member.NumberFormat); } catch { /* not a member */ }
            try { parent = Blank(member.ParentHierarchy); } catch { /* not a member */ }

            int? position = null;
            var caption = LastSegment(uniqueName);
            foreach (Xl.CubeField cf in pivot.CubeFields)
            {
                if (!string.Equals(cf.Name, uniqueName, StringComparison.OrdinalIgnoreCase)) continue;
                try
                {
                    if (cf.Orientation == Xl.XlPivotFieldOrientation.xlDataField)
                    {
                        position = cf.Position;
                        caption = cf.Caption;
                    }
                }
                catch { /* position unknown: the measure comes back at the end */ }
                break;
            }

            return new PreviousCalculation(
                member, member.Formula, member.SolveOrder, kind, folder, format, parent, caption, position);
        }
        return null;
    }

    private static void Recreate(Xl.PivotTable pivot, string uniqueName, PreviousCalculation previous)
    {
        try
        {
            Create(pivot, uniqueName, previous.Caption, previous.Formula, previous.SolveOrder,
                previous.Kind, previous.DisplayFolder, previous.NumberFormat, previous.ParentHierarchy);

            if (previous.DataPosition is int position)
            {
                ShowMeasure(pivot, uniqueName, previous.Caption);
                TryRestorePosition(pivot, uniqueName, position);
            }
        }
        catch (Exception ex)
        {
            FileLog.Write($"Could not restore the previous version of '{uniqueName}'. " +
                          $"Formula was: {previous.Formula}", ex);
        }
    }

    private static Xl.CalculatedMember Create(
        Xl.PivotTable pivot, string uniqueName, string caption, string formula, int solveOrder,
        CalculationKind kind, string? displayFolder, string? numberFormat, string? parentHierarchy)
    {
        if (kind is CalculationKind.Set)
        {
            var set = pivot.CalculatedMembers.Add(
                uniqueName, formula, solveOrder, Xl.XlCalculatedMemberType.xlCalculatedSet);
            // Documented: a set only appears after AddSet.
            pivot.CubeFields.AddSet(uniqueName, caption);
            return set;
        }

        return pivot.CalculatedMembers.AddCalculatedMember(
            Name: uniqueName,
            Formula: formula,
            SolveOrder: solveOrder,
            Type: kind is CalculationKind.Measure
                ? Xl.XlCalculatedMemberType.xlCalculatedMeasure
                : Xl.XlCalculatedMemberType.xlCalculatedMember,
            DisplayFolder: (object?)displayFolder ?? Type.Missing,
            MeasureGroup: Type.Missing,
            ParentHierarchy: (object?)parentHierarchy ?? Type.Missing,
            ParentMember: Type.Missing,
            // An enumeration, not a format string (see CalculationNumberFormat).
            NumberFormat: CalculationNumberFormat.ToExcel(numberFormat) is int format
                ? (Xl.XlCalcMemNumberFormatType)format
                : Type.Missing);
    }

    private static void TryRestorePosition(Xl.PivotTable pivot, string uniqueName, int position)
    {
        foreach (Xl.CubeField cf in pivot.CubeFields)
        {
            if (!string.Equals(cf.Name, uniqueName, StringComparison.OrdinalIgnoreCase)) continue;
            try { cf.Position = position; }
            catch (Exception ex) { FileLog.Write($"Could not restore the position of '{uniqueName}'.", ex); }
            return;
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>"[Measures].[Marge]" → "Marge".</summary>
    private static string LastSegment(string uniqueName)
    {
        var last = uniqueName.LastIndexOf('[');
        return last < 0 ? uniqueName : uniqueName[(last + 1)..].TrimEnd(']');
    }

    public static void Delete(string uniqueName)
    {
        var pivot = RequirePivot();
        if (!DeleteIfExists(pivot, uniqueName))
            throw new InvalidOperationException($"Calcul introuvable : {uniqueName}");
    }

    /// <summary>
    /// IsValid lies on a disconnected PivotTable (documented): it returns True. So
    /// we connect before any check.
    /// </summary>
    private static void EnsureConnected(Xl.PivotTable pivot)
    {
        try
        {
            var cache = pivot.PivotCache();
            if (!cache.IsConnected) cache.MakeConnection();
        }
        catch (Exception ex)
        {
            FileLog.Write("Could not restore the PivotTable cache connection.", ex);
        }
    }

    private static bool DeleteIfExists(Xl.PivotTable pivot, string uniqueName)
    {
        foreach (Xl.CalculatedMember member in pivot.CalculatedMembers)
        {
            if (!string.Equals(member.Name, uniqueName, StringComparison.OrdinalIgnoreCase))
                continue;
            member.Delete();
            return true;
        }
        return false;
    }

    /// <summary>
    /// Places the calculated measure in the values area. AddDataField expects a
    /// CUBE FIELD, not the member; GetMeasure does not fit here (it only serves
    /// implicit measures of an attribute hierarchy, and only for
    /// Count/Sum/Average/Max/Min).
    /// </summary>
    private static void ShowMeasure(Xl.PivotTable pivot, string uniqueName, string caption)
    {
        if (TryAddDataField(pivot, uniqueName, caption)) return;

        // Excel only materializes the CubeField of a session measure after a
        // refresh: the CubeFields collection reflects the last state fetched
        // from the server. That is why the original add-in offered a
        // "Refresh data by default".
        try
        {
            var cache = pivot.PivotCache();
            if (!cache.EnableRefresh)
                throw new InvalidOperationException(
                    "Le rafraîchissement automatique est coupé : la mesure a été créée " +
                    "mais ne peut pas être ajoutée au tableau. Réactivez-le dans " +
                    "l'onglet Construction, puis relancez.");

            pivot.RefreshTable();
        }
        catch (InvalidOperationException) { throw; }
        catch (Exception ex)
        {
            FileLog.Write("Refresh failed after creating the calculation.", ex);
        }

        if (TryAddDataField(pivot, uniqueName, caption)) return;

        // The calculation exists but its cube field cannot be found: rather than
        // fail blindly, log the actual inventory. This reflex has
        // already solved two interop bugs in phase 1.
        var inventory = new StringBuilder();
        foreach (Xl.CubeField cf in pivot.CubeFields)
            inventory.Append($"\n  {cf.Name} | type={cf.CubeFieldType} | sub={cf.CubeFieldSubType}");

        FileLog.Write(
            $"Calculated measure '{uniqueName}' created, but its CubeField was " +
            $"not found. Inventory:{inventory}");
    }

    private static bool TryAddDataField(Xl.PivotTable pivot, string uniqueName, string caption)
    {
        foreach (Xl.CubeField cf in pivot.CubeFields)
        {
            if (!string.Equals(cf.Name, uniqueName, StringComparison.OrdinalIgnoreCase))
                continue;
            if (cf.Orientation != Xl.XlPivotFieldOrientation.xlDataField)
                pivot.AddDataField(cf, caption, Type.Missing);
            return true;
        }
        return false;
    }

    private static string SafeFormula(Xl.CalculatedMember member)
    {
        try { return member.Formula ?? string.Empty; } catch { return string.Empty; }
    }

    private static string KindLabel(Xl.CalculatedMember member)
    {
        try
        {
            return member.Type switch
            {
                Xl.XlCalculatedMemberType.xlCalculatedMeasure => nameof(CalculationKind.Measure),
                Xl.XlCalculatedMemberType.xlCalculatedSet => nameof(CalculationKind.Set),
                _ => nameof(CalculationKind.Member),
            };
        }
        catch { return nameof(CalculationKind.Member); }
    }
}
