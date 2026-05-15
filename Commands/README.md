# Commands

Remote Admin commands for displaying and exporting OBJ models.

## How Commands Work

All commands implement `ICommand` for the EXILED Remote Admin handler. Each display command stores its model instance and follows a toggle pattern:

- **First run:** loads the model and starts building (spawning primitives in batches via coroutine).
- **Run while building:** cancels the build.
- **Run after built:** destroys the model.

Export commands write ProjectMER schematic JSON files to the LabAPI schematics folder.

All NGon commands read their defaults from `NGonModelConfig.Session`, which can be changed at runtime via `NGonConfig`.

## Files

| File                              | Command                 | Description                                                                            |
|-----------------------------------|-------------------------|----------------------------------------------------------------------------------------|
| `TriangulateNGonV3Command.cs`     | `TriangulateNGonV3`     | N-gon decomposition with V3 hierarchical parenting. **Lowest primitive count.**        |
| `TriangulateNGonV2Command.cs`     | `TriangulateNGonV2`     | N-gon decomposition with V2 stretch clustering.                                        |
| `TriangulateNGonCommand.cs`       | `TriangulateNGon`       | N-gon decomposition with V1 exact parallelograms.                                      |
| `TriangulateV3Command.cs`         | `TriangulateV3`         | Triangle-based V3 hierarchical model.                                                  |
| `TriangulateV2Command.cs`         | `TriangulateV2`         | Triangle-based V2 model.                                                               |
| `TriangulateCommand.cs`           | `Triangulate`           | Triangle-based V1 model.                                                               |
| `ExportSchematicNGonV3Command.cs` | `ExportSchematicNGonV3` | Export N-gon V3 model to ProjectMER JSON. **Lowest primitive count.**                  |
| `ExportSchematicNGonV2Command.cs` | `ExportSchematicNGonV2` | Export N-gon V2 model to ProjectMER JSON.                                              |
| `ExportSchematicNGonCommand.cs`   | `ExportSchematicNGon`   | Export N-gon V1 model to ProjectMER JSON.                                              |
| `ExportSchematicV3Command.cs`     | `ExportSchematicV3`     | Export triangle V3 model to ProjectMER JSON.                                           |
| `ExportSchematicV2Command.cs`     | `ExportSchematicV2`     | Export triangle V2 model to ProjectMER JSON.                                           |
| `ExportSchematicCommand.cs`       | `ExportSchematic`       | Export triangle V1 model to ProjectMER JSON.                                           |
| `NGonConfigCommand.cs`            | `NGonConfig`            | Get/set session config values for the N-gon pipeline at runtime.                       |
| `TriangleExampleCommand.cs`       | `TriangleExample`       | Debug: spawns a random triangle with colored vertex markers.                           |
| `TestParallelogramsCommand.cs`    | `TestParallelograms`    | Debug: visualizes V2 stretch clustering with random parallelograms.                    |
| `TestNGonsCommand.cs`             | `TestNGons`             | Debug: visualizes N-gon pipeline stages (0=raw, 1=merged, 2=convex, 3=parallelograms). |
