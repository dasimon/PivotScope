namespace PivotScope.Core.Abstractions;

/// <summary>Un membre d'un niveau : ce que l'utilisateur voit, et ce que MDX adresse.</summary>
public sealed record LevelMember(string Caption, string UniqueName);

/// <summary>
/// Énumération des membres d'un niveau, pour résoudre des libellés.
///
/// Mesuré sur un cube réel : 3 157 membres d'un niveau en 79 ms. Le piège
/// documenté sur CubeScope — « ne jamais scanner » — vise
/// $SYSTEM.MDSCHEMA_MEMBERS, qui parcourt la dimension entière ; énumérer un
/// seul niveau en MDX est d'un tout autre ordre de grandeur.
/// </summary>
public interface ILevelMemberReader
{
    Task<IReadOnlyList<LevelMember>> GetLevelMembersAsync(
        string cube, string levelUniqueName, int limit, CancellationToken ct = default);
}
