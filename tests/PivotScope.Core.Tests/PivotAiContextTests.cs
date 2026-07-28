using PivotScope.Core.Ai;
using PivotScope.Core.Models;

namespace PivotScope.Core.Tests;

public class PivotAiContextTests
{
    private static PivotContext Pivot(params PivotFieldInfo[] fields)
        => new(true, true, "SRV", "CAT", "Ventes", "SELECT …", fields, null);

    [Fact]
    public void Describe_SansTcd_RendUneChaineVide()
        => Assert.Empty(PivotAiContext.Describe(PivotContext.None("pas de TCD")));

    [Fact]
    public void Describe_ContexteNul_RendUneChaineVide()
        => Assert.Empty(PivotAiContext.Describe(null));

    [Fact]
    public void Describe_NommeLeCube()
        => Assert.Contains("Ventes", PivotAiContext.Describe(Pivot()));

    [Fact]
    public void Describe_GroupeLesChampsParZone()
    {
        var text = PivotAiContext.Describe(Pivot(
            new PivotFieldInfo("Devise", "[Devise].[Devise]", "row"),
            new PivotFieldInfo("Année", "[Temps].[Année]", "column"),
            new PivotFieldInfo("Catégorie", "[Catégorie].[Catégorie]", "filter"),
            new PivotFieldInfo("VL", "[Measures].[VL]", "data")));

        Assert.Contains("Champs en ligne : Devise", text);
        Assert.Contains("Champs en colonne : Année", text);
        Assert.Contains("Filtres de rapport : Catégorie", text);
        Assert.Contains("Mesures affichées : VL", text);
    }

    [Fact]
    public void Describe_PlusieursChampsDansUneZone_LesJoint()
    {
        var text = PivotAiContext.Describe(Pivot(
            new PivotFieldInfo("Devise", "[a]", "row"),
            new PivotFieldInfo("Fonds", "[b]", "row")));

        Assert.Contains("Champs en ligne : Devise, Fonds", text);
    }

    [Fact]
    public void Describe_ZoneVide_NEstPasMentionnee()
    {
        var text = PivotAiContext.Describe(Pivot(
            new PivotFieldInfo("Devise", "[a]", "row")));

        Assert.DoesNotContain("Champs en colonne", text);
    }

    [Fact]
    public void Describe_TableauSansChamp_LeDitExplicitement()
    {
        // Une section vide serait ambiguë pour le modèle : mieux vaut l'écrire.
        Assert.Contains("Aucun champ posé", PivotAiContext.Describe(Pivot()));
    }
}
