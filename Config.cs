using Exiled.API.Interfaces;

namespace TriangleScpSl;

public sealed class Config : IConfig
{
    public bool IsEnabled { get; set; } = true;
    public bool Debug { get; set; } = false;
    public int TriangulateBuildBatchSize { get; set; } = 128;
    public int TriangulateV2BuildBatchSize { get; set; } = 64;
    public int ExportBuildBatchSize { get; set; } = 128;
    public int ExportWriteBatchSize { get; set; } = 512;
    public int TriangulateNGonBuildBatchSize { get; set; } = 128;
    public int TriangulateNGonV2BuildBatchSize { get; set; } = 64;
    public int TriangulateNGonOptBuildBatchSize { get; set; } = 128;
}