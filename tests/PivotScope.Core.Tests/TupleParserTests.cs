using PivotScope.Core.Provenance;

namespace PivotScope.Core.Tests;

public class TupleParserTests
{
    [Fact]
    public void Parse_SepareLaMesureDesCoordonnees()
    {
        var tuple = TupleParser.Parse(
            "([Measures].[Chiffre d'affaires],[Devise].[Devise].&[EUR])");

        Assert.Equal("[Measures].[Chiffre d'affaires]", tuple.Measure);
        Assert.Equal(["[Devise].[Devise].&[EUR]"], tuple.Coordinates);
    }

    [Fact]
    public void Parse_SansParenthesesEnglobantes()
    {
        var tuple = TupleParser.Parse("[Measures].[VL]");

        Assert.Equal("[Measures].[VL]", tuple.Measure);
        Assert.Empty(tuple.Coordinates);
    }

    [Fact]
    public void Parse_NeCoupePasSurUneVirguleDansUnCrochet()
    {
        // Un libellé de membre peut contenir une virgule : découper naïvement
        // sur « , » casserait la coordonnée en deux.
        var tuple = TupleParser.Parse(
            "([Measures].[VL],[Fonds].[Fonds].&[Actions, Europe])");

        Assert.Equal(["[Fonds].[Fonds].&[Actions, Europe]"], tuple.Coordinates);
    }

    [Fact]
    public void Parse_SansMesure_RendMeasureNull()
    {
        var tuple = TupleParser.Parse("([Devise].[Devise].&[EUR])");

        Assert.Null(tuple.Measure);
        Assert.Single(tuple.Coordinates);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("()")]
    public void Parse_EntreeVide_NeLevePas(string input)
    {
        var tuple = TupleParser.Parse(input);

        Assert.Null(tuple.Measure);
        Assert.Empty(tuple.Coordinates);
    }

    [Fact]
    public void Parse_ToleLesEspacesAutourDesVirgules()
    {
        var tuple = TupleParser.Parse("( [Measures].[VL] , [Devise].[Devise].&[EUR] )");

        Assert.Equal("[Measures].[VL]", tuple.Measure);
        Assert.Equal(["[Devise].[Devise].&[EUR]"], tuple.Coordinates);
    }

    [Fact]
    public void Parse_PlusieursCoordonnees_LesGardeDansLOrdre()
    {
        var tuple = TupleParser.Parse(
            "([Measures].[VL],[Devise].[Devise].&[EUR],[Temps].[Année].&[2026])");

        Assert.Equal(
            ["[Devise].[Devise].&[EUR]", "[Temps].[Année].&[2026]"],
            tuple.Coordinates);
    }

    [Fact]
    public void Parse_MesurePasEnPremierePosition_EstQuandMemeReconnue()
    {
        var tuple = TupleParser.Parse(
            "([Devise].[Devise].&[EUR],[Measures].[VL])");

        Assert.Equal("[Measures].[VL]", tuple.Measure);
        Assert.Equal(["[Devise].[Devise].&[EUR]"], tuple.Coordinates);
    }
}
