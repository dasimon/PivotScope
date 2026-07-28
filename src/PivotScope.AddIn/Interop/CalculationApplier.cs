using System.Text;
using ExcelDna.Integration;
using PivotScope.AddIn.Diagnostics;
using PivotScope.Core.Calculations;
using Xl = Microsoft.Office.Interop.Excel;

namespace PivotScope.AddIn.Interop;

/// <summary>Un calcul déjà présent sur le TCD.</summary>
public sealed record ExistingCalculation(
    string Name, string Formula, string Kind, bool IsValid, string? DisplayFolder);

/// <summary>
/// Crée, liste et supprime les calculs d'un TCD OLAP.
///
/// Points documentés qu'il faut respecter, sous peine d'erreurs opaques ou de
/// réglages silencieusement ignorés :
/// — depuis Excel 2013, mesures et membres passent par AddCalculatedMember ;
///   seuls les ENSEMBLES nommés utilisent encore Add, suivi de CubeFields.AddSet ;
/// — DisplayFolder n'est valide que pour une mesure, NumberFormat que pour un
///   membre (validé en amont par CalculationValidator) ;
/// — IsValid renvoie True quand le TCD n'est pas connecté : il faut appeler
///   PivotCache.MakeConnection() avant de s'y fier, sinon on déclare valide un
///   calcul cassé.
///
/// À appeler exclusivement via <see cref="ExcelThread"/>.
/// </summary>
public static class CalculationApplier
{
    private static Xl.PivotTable RequirePivot()
    {
        var app = (Xl.Application)ExcelDnaUtil.Application;
        Xl.PivotTable? pivot = null;
        try { pivot = app.ActiveCell?.PivotTable; } catch { /* hors TCD */ }

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
            try { folder = member.DisplayFolder; } catch { /* pas une mesure */ }

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

        // Remplacer plutôt que d'échouer sur un doublon : c'est le geste attendu
        // quand on met au point une expression.
        DeleteIfExists(pivot, uniqueName);

        Xl.CalculatedMember created;
        if (definition.Kind is CalculationKind.Set)
        {
            created = pivot.CalculatedMembers.Add(
                uniqueName, definition.Expression, definition.SolveOrder,
                Xl.XlCalculatedMemberType.xlCalculatedSet);
            // Documenté : un ensemble n'apparaît qu'après AddSet.
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
    /// IsValid ment sur un TCD déconnecté (documenté) : il renvoie True. On se
    /// connecte donc avant toute vérification.
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
    /// Pose la mesure calculée dans la zone de valeurs. AddDataField attend un
    /// CUBE FIELD, pas le membre ; GetMeasure ne convient pas ici (il ne sert
    /// qu'aux mesures implicites d'une hiérarchie d'attribut, et seulement pour
    /// Count/Sum/Average/Max/Min).
    /// </summary>
    private static void ShowMeasure(Xl.PivotTable pivot, string uniqueName, string caption)
    {
        if (TryAddDataField(pivot, uniqueName, caption)) return;

        // Excel ne matérialise le CubeField d'une mesure de session qu'après un
        // rafraîchissement : la collection CubeFields reflète le dernier état
        // rapatrié du serveur. C'est pour cette raison que l'add-in d'origine
        // proposait un « Refresh data by default ».
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

        // Le calcul existe mais son cube field est introuvable : plutôt que
        // d'échouer en aveugle, on journalise l'inventaire réel. Ce réflexe a
        // déjà résolu deux bugs d'interop en phase 1.
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
