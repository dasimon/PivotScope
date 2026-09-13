using CubeScope.Core.Models;

namespace PivotScope.Core.Abstractions;

/// <summary>Executes an arbitrary MDX query against the current cube.</summary>
public interface IMdxExecutor
{
    Task<QueryResult> ExecuteAsync(string mdx, CancellationToken ct = default);
}
