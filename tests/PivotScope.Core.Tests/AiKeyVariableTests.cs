using CubeScope.Core.Ai;
using PivotScope.Core.Adapters;

namespace PivotScope.Core.Tests;

/// <summary>
/// The pane names the environment variable that holds the Anthropic key; PivotScope keeps the
/// name in its own state database and applies it to the Excel process. Never the key itself.
/// </summary>
public sealed class AiKeyVariableTests
{
    private static string NewPath() => Path.Combine(Path.GetTempPath(), $"ps-aikey-{Guid.NewGuid():N}.db");

    [Fact]
    public void A_chosen_name_is_stored_and_applied_then_a_blank_one_goes_back_to_the_default()
    {
        var before = AiService.KeyVariable;
        var path = NewPath();
        try
        {
            Assert.Equal("CLAUDE_ESSAIS_API_KEY", CubeScopeSession.SetAiKeyVariable(" CLAUDE_ESSAIS_API_KEY ", path));
            Assert.Equal("CLAUDE_ESSAIS_API_KEY", CubeScopeSession.AiKeyVariable);
            Assert.Equal("CLAUDE_ESSAIS_API_KEY", AiService.KeyVariable);

            Assert.Equal("ANTHROPIC_API_KEY", CubeScopeSession.SetAiKeyVariable("", path));
            Assert.Equal("ANTHROPIC_API_KEY", CubeScopeSession.AiKeyVariable);
        }
        finally
        {
            AiService.KeyVariable = before;
        }
    }

    [Fact]
    public void An_invalid_name_is_refused_with_a_message_for_the_user()
    {
        var before = AiService.KeyVariable;
        var ex = Assert.Throws<ArgumentException>(() => CubeScopeSession.SetAiKeyVariable("MA-CLE", NewPath()));
        Assert.Contains("Nom de variable invalide", ex.Message, StringComparison.Ordinal);
        Assert.Equal(before, AiService.KeyVariable);
    }
}
