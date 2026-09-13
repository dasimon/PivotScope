namespace PivotScope.Core.Abstractions;

/// <summary>A member of a level: what the user sees, and what MDX addresses.</summary>
public sealed record LevelMember(string Caption, string UniqueName);

/// <summary>
/// Enumerates the members of a level, to resolve captions.
///
/// Measured on a real cube: 3,157 members of a level in 79 ms. The pitfall
/// documented in CubeScope — "never scan" — targets
/// $SYSTEM.MDSCHEMA_MEMBERS, which walks the entire dimension; enumerating a
/// single level in MDX is a whole different order of magnitude.
/// </summary>
public interface ILevelMemberReader
{
    Task<IReadOnlyList<LevelMember>> GetLevelMembersAsync(
        string cube, string levelUniqueName, int limit, CancellationToken ct = default);
}
