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
        // while fine-tuning an expression.
        DeleteIfExists(pivot, uniqueName);

        Xl.CalculatedMember created;
        if (definition.Kind is CalculationKind.Set)
        {
            created = pivot.CalculatedMembers.Add(
                uniqueName, definition.Expression, definition.SolveOrder,
                Xl.XlCalculatedMemberType.xlCalculatedSet);
            // Documented: a set only appears after AddSet.
            pivot.CubeFields.AddSet(uniqueName, definition.Name.Trim());
        }
        else
        {
            created = pivot.CalculatedMembers.AddCalculatedMember(
                Name: uniqueName,
                Formula: definition.Expression,
                SolveOrder: definition.SolveOrder,
                Type: definition.Kind is CalculationKind.Measure
                    ? Xl.XlCalculatedMemberType.xlCalculatedMeasure
                    : Xl.XlCalculatedMemberType.xlCalculatedMember,
                DisplayFolder: (object?)definition.DisplayFolder ?? Type.Missing,
                MeasureGroup: Type.Missing,
                ParentHierarchy: (object?)definition.ParentHierarchy ?? Type.Missing,
                ParentMember: Type.Missing,
                NumberFormat: (object?)definition.NumberFormat ?? Type.Missing);
        }

        if (!created.IsValid)
        {
            created.Delete();
            throw new InvalidOperationException(
                "Le serveur a refusé ce calcul : vérifiez l'expression MDX.");
        }

        if (addToPivot && definition.Kind is CalculationKind.Measure)
            ShowMeasure(pivot, uniqueName, definition.Name.Trim());

        return uniqueName;
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
            FileLog.Write("Impossible de rétablir la connexion du cache du TCD.", ex);
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
            FileLog.Write("Échec du rafraîchissement après création du calcul.", ex);
        }

        if (TryAddDataField(pivot, uniqueName, caption)) return;

        // The calculation exists but its cube field cannot be found: rather than
        // fail blindly, log the actual inventory. This reflex has
        // already solved two interop bugs in phase 1.
        var inventory = new StringBuilder();
        foreach (Xl.CubeField cf in pivot.CubeFields)
            inventory.Append($"\n  {cf.Name} | type={cf.CubeFieldType} | sub={cf.CubeFieldSubType}");

        FileLog.Write(
            $"Mesure calculée « {uniqueName} » créée, mais son CubeField est " +
            $"introuvable. Inventaire :{inventory}");
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
                Xl.XlCalculatedMemberType.xlCalculatedMeasure => "mesure",
                Xl.XlCalculatedMemberType.xlCalculatedSet => "ensemble",
                _ => "membre",
            };
        }
        catch { return "membre"; }
    }
}
