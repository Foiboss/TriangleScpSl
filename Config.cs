using Exiled.API.Interfaces;

namespace TriangleScpSl;

public sealed class Config : IConfig
{
    public bool IsEnabled { get; set; } = true;
    public bool Debug { get; set; } = false;
    public int TriangulateBuildBatchSize { get; set; } = 32;
    public int TriangulateV2BuildBatchSize { get; set; } = 16;
    public int ExportBuildBatchSize { get; set; } = 64;
    public int ExportWriteBatchSize { get; set; } = 256;
}