using CubeScope.Core.Models;

namespace PivotScope.Core.Abstractions;

/// <summary>Exécution d'une requête MDX arbitraire sur le cube courant.</summary>
public interface IMdxExecutor
{
    Task<QueryResult> ExecuteAsync(string mdx, CancellationToken ct = default);
}
