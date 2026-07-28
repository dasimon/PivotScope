using CubeScope.Core.Models;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Tests;

/// <summary>
/// Prouve que le sous-module CubeScope est bien référencé et que ses types
/// traversent la frontière d'assembly via nos abstractions.
/// </summary>
public class AbstractionsTests
{
    [Fact]
    public void ICubeMetadataReader_ExposesCubeMeta_FromCubeScope()
    {
        var method = typeof(ICubeMetadataReader)
            .GetMethod(nameof(ICubeMetadataReader.GetCubeMetaAsync))!;

        Assert.Equal(typeof(Task<CubeMeta>), method.ReturnType);
    }

    [Fact]
    public void IMdxExecutor_ExposesQueryResult_FromCubeScope()
    {
        var method = typeof(IMdxExecutor)
            .GetMethod(nameof(IMdxExecutor.ExecuteAsync))!;

        Assert.Equal(typeof(Task<QueryResult>), method.ReturnType);
    }
}
