using CubeScope.Core.Models;
using PivotScope.Core.Abstractions;

namespace PivotScope.Core.Tests;

/// <summary>
/// Proves that the CubeScope submodule is correctly referenced and that its
/// types cross the assembly boundary through our abstractions.
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
