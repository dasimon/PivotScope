using PivotScope.Core.Calculations;

namespace PivotScope.Core.Tests;

public class CalculationValidatorTests
{
    private static CalculationDefinition Measure(string name, string expression = "1")
        => new(name, expression, CalculationKind.Measure);

    [Theory]
    [InlineData("Marge")]
    [InlineData("Marge nette")]
    [InlineData("VL pondérée")]
    [InlineData("Ratio 2026")]
    public void Validate_AccepteLesNomsOrdinaires(string name)
        => Assert.Empty(CalculationValidator.Validate(Measure(name)));

    [Fact]
    public void Validate_RefuseUnNomVide()
        => Assert.Contains(
            CalculationValidator.Validate(Measure("   ")),
            m => m.Contains("nom", StringComparison.OrdinalIgnoreCase));

    [Fact]
    public void Validate_RefuseUneExpressionVide()
        => Assert.Contains(
            CalculationValidator.Validate(Measure("Marge", "  ")),
            m => m.Contains("expression", StringComparison.OrdinalIgnoreCase));

    [Theory]
    [InlineData("Ma[rge")]
    [InlineData("Marge]")]
    public void Validate_RefuseLesCrochetsDansLeNom(string name)
    {
        // A bracket would break the MDX unique name built around it.
        Assert.NotEmpty(CalculationValidator.Validate(Measure(name)));
    }

    [Fact]
    public void Validate_UnMembreExigeSaHierarchieParente()
    {
        var member = new CalculationDefinition("Zone euro", "1", CalculationKind.Member);

        Assert.Contains(
            CalculationValidator.Validate(member),
            m => m.Contains("hiérarchie", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_UneMesureNAPasBesoinDeHierarchieParente()
        => Assert.Empty(CalculationValidator.Validate(Measure("Marge")));

    [Fact]
    public void Validate_RefuseUnFormatSurAutreChoseQuUnMembre()
    {
        // Documented: NumberFormat is only valid for calculated members.
        var measure = new CalculationDefinition(
            "Marge", "1", CalculationKind.Measure, NumberFormat: "#,##0.00");

        Assert.Contains(
            CalculationValidator.Validate(measure),
            m => m.Contains("format", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_RefuseUnDossierSurAutreChoseQuUneMesure()
    {
        // Documented: DisplayFolder is only valid for calculated measures.
        var member = new CalculationDefinition(
            "Zone euro", "1", CalculationKind.Member,
            DisplayFolder: "Devises", ParentHierarchy: "[Devise].[Devise]");

        Assert.Contains(
            CalculationValidator.Validate(member),
            m => m.Contains("dossier", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_CumuleLesMessages()
    {
        var broken = new CalculationDefinition("", "", CalculationKind.Member);

        Assert.Equal(3, CalculationValidator.Validate(broken).Count);
    }

    [Fact]
    public void QualifiedName_UneMesureVitSousMeasures()
        => Assert.Equal("[Measures].[Marge]",
            CalculationValidator.QualifiedName(Measure("Marge")));

    [Fact]
    public void QualifiedName_UnMembreVitSousSaHierarchie()
    {
        var member = new CalculationDefinition(
            "Zone euro", "1", CalculationKind.Member, ParentHierarchy: "[Devise].[Devise]");

        Assert.Equal("[Devise].[Devise].[Zone euro]",
            CalculationValidator.QualifiedName(member));
    }

    [Fact]
    public void QualifiedName_ElagueLeNom()
        => Assert.Equal("[Measures].[Marge]",
            CalculationValidator.QualifiedName(Measure("  Marge  ")));
}
