namespace Triangle.Core.Paths;

public static class TrianglePaths
{
    const string SlFolderName = "SCP Secret Laboratory";
    const string LabApiFolderName = "LabAPI";
    const string LabApiConfigsFolderName = "configs";
    const string MerFolderName = "ProjectMER";
    const string MerSchematicsFolderName = "Schematics";

    public const string ModelsFolderName = "BlenderModels";

    public static string ModelsFolderPath => Path.Combine(Exiled.API.Features.Paths.Plugins, ModelsFolderName);

    public static string MerSchematicsRootPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        SlFolderName,
        LabApiFolderName,
        LabApiConfigsFolderName,
        MerFolderName,
        MerSchematicsFolderName);

    public static void EnsureModelsFolderExists() => Directory.CreateDirectory(ModelsFolderPath);

    public static string GetModelPath(string fileName) => Path.Combine(ModelsFolderPath, fileName);

    public static string GetSchematicFolderName(string outputFileName) => Path.GetFileNameWithoutExtension(outputFileName);

    public static string GetSchematicDirectory(string outputFileName) =>
        Path.Combine(MerSchematicsRootPath, GetSchematicFolderName(outputFileName));

    public static string EnsureSchematicDirectoryExists(string outputFileName)
    {
        string directory = GetSchematicDirectory(outputFileName);
        Directory.CreateDirectory(directory);
        return directory;
    }

    public static string GetSchematicOutputPath(string outputFileName) =>
        Path.Combine(GetSchematicDirectory(outputFileName), outputFileName);
}

