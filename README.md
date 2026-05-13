# TriangleScpSl

![blender-monkey](https://github.com/user-attachments/assets/2012cb09-db5a-4140-a48f-e1e865e89234)

An [EXILED](https://github.com/ExMod-Team/EXILED) plugin for SCP: Secret Laboratory that renders 3D meshes from OBJ files in the game world using Unity primitive toys (Quads, Spheres, Cylinders, Cubes).

- **Plugin:** `TriangleScpSl` v4.0.0
- **Author:** Foibos
- **Framework:** net48 / EXILED 9.13.3
- **License:** CC-BY-SA 3.0 (required by EXILED)

---

## Quick Start

[![Download Latest Release](https://img.shields.io/badge/download-latest%20release-brightgreen?style=for-the-badge)](https://github.com/Foiboss/TriangleScpSl/releases/latest)

1. Build the project or grab the latest release.
2. Copy `TriangleScpSl.dll` into `EXILED/Plugins/`.
3. Place your `.obj` (and optional `.mtl`) files in `EXILED/Plugins/BlenderModels/`.
4. In Remote Admin, run the command to display or export your model.

---

## How It Works

The plugin converts OBJ mesh faces into Unity primitives that can be spawned in SCP:SL.

### The Key Trick: Parallelogram Decomposition

Any convex polygon can be decomposed into parallelograms. A parallelogram can be rendered using Unity Quads by exploiting the **SetParent deformation trick**: a child primitive inherits the non-uniform scale of its invisible parent, producing a sheared shape.

### Optimization Layers

The plugin applies several tricks to minimize the number of primitives needed:

| Optimization                  | What it does                                                                            | Savings                                                        |
|-------------------------------|-----------------------------------------------------------------------------------------|----------------------------------------------------------------|
| **N-gon processing**          | Works with original polygon faces instead of triangulating first                        | Use of N parallelograms for one N-gon                          |
| **Coplanar face merging**     | Merges adjacent same-color faces on the same plane into larger polygons                 | Fewer, bigger faces to decompose                               |
| **Rectangle detection**       | When a parallelogram has equal-length diagonals, uses 1 Quad instead of 2               | Up to 50% on box-like geometry                                 |
| **Primitive shape detection** | Detects spheres, cylinders, cubes in the mesh and replaces them with 1 native primitive | Use of 1 primitive instead of parallelogram decomposition      |
| **Hidden tail optimization**  | Hides parallelogram tails inside solid material, avoiding extra primitives              | ~10-30% on complex models                                      |
| **Stretch clustering (V2)**   | Groups similarly-oriented parallelograms under shared stretch primitives                | Significant on dense meshes, but works as well on smaller ones |

### Two Rendering Pipelines

**V1 (Exact):** Each parallelogram = 2 Quads (base + visible child). Pixel-perfect.

**V2 (Approximate):** Groups parallelograms by angular similarity under shared "stretch" primitives. Fewer total primitives at the cost of tiny vertex error (configurable accuracy parameter).

---

## Best Command to Use

> ### `TriangulateNGonV2` - the recommended display command
>
> This command applies the N-gon pipeline with V2 stretch clustering. Enable all optimizations
> (hidden tails, primitive detection, smoothness) via `NGonConfig` for the lowest primitive count.
>
> ```
> TriangulateNGonV2 <model.obj> [planar threshold] [accuracy]
> ```
>
> For export, use `ExportSchematicNGonV2` with the same pipeline.

---

## All Commands

Every command can be run again while building to cancel, or run again after the model is visible to destroy it.

### Display Commands (player only)

| Command                                        | Pipeline      | Description                                                              |
|------------------------------------------------|---------------|--------------------------------------------------------------------------|
| `TriangulateNGonV2 <file> [planar] [accuracy]` | N-gon + V2    | **Best result.** N-gon decomposition with stretch clustering.            |
| `TriangulateNGon <file> [planar]`              | N-gon + V1    | N-gon decomposition with exact parallelograms.                           |
| `TriangulateV2 <file> [accuracy]`              | Triangle + V2 | Triangle-based with stretch clustering. Simpler, higher primitive count. |
| `Triangulate <file>`                           | Triangle + V1 | Simplest pipeline. Triangle-based, exact parallelograms.                 |

### Export Commands (also works in server console)

| Command                                                     | Pipeline      | Description                                          |
|-------------------------------------------------------------|---------------|------------------------------------------------------|
| `ExportSchematicNGonV2 <file> <output> [planar] [accuracy]` | N-gon + V2    | **Best export.** Same pipeline as TriangulateNGonV2. |
| `ExportSchematicNGon <file> <output> [planar]`              | N-gon + V1    | Export N-gon V1 to ProjectMER schematic JSON.        |
| `ExportSchematicV2 <file> <output> [accuracy]`              | Triangle + V2 | Export triangle V2 to ProjectMER schematic JSON.     |
| `ExportSchematic <file> <output>`                           | Triangle + V1 | Export triangle V1 to ProjectMER schematic JSON.     |

### Config & Debug Commands

| Command                         | Description                                                                     |
|---------------------------------|---------------------------------------------------------------------------------|
| `NGonConfig [property] [value]` | View or change session config for the N-gon pipeline (see below).               |
| `TriangleExample`               | Spawns a random triangle with colored vertex markers.                           |
| `TestParallelograms [count]`    | Visualizes V2 stretch clustering with random parallelograms.                    |
| `TestNGons <file> [stage]`      | Visualizes N-gon pipeline stages (0=raw, 1=merged, 2=convex, 3=parallelograms). |

---

## Session Config (`NGonConfig`)

The NGon pipeline is configured through `NGonModelConfig`, a session-scoped config that persists until the server restarts. Use the `NGonConfig` RA command to view and change values at runtime. All subsequent NGon commands (`TriangulateNGon`, `TriangulateNGonV2`, and their export variants) will use
the updated defaults.

```
NGonConfig                          -- list all current values
NGonConfig PlanarThreshold          -- get a single value
NGonConfig PlanarThreshold 0.01     -- set a value for this session
```

### Config Properties

| Property                        | Type  | Default | Effect on Quality and Primitive Count                                                                                                                                                                                                                                                                                                    |
|---------------------------------|-------|---------|------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------|
| `UseHiddenTailOptimization`     | bool  | `True`  | Hides parallelogram tail triangles inside solid geometry. Saves ~10-30% primitives on complex models.                                                                                                                                                                                                                                    |
| `DetectPrimitives`              | bool  | `True`  | Detects spheres, cylinders, cubes and replaces them with 1 native primitive each.                                                                                                                                                                                                                                                        |
| `Accuracy`                      | float | `0.001` | Max vertex error (world units) for V2 stretch clustering. Lower = more precise but more primitives. Only affects V2 commands.                                                                                                                                                                                                            |
| `SmoothMaxAngle`                | float | `0.32`  | Max dihedral angle (radians, ~18 deg) between adjacent faces to consider a surface smooth. Controls how aggressively primitive shapes are detected. Higher = more shape detection.                                                                                                                                                       |
| `SmoothMinFraction`             | float | `0.7`   | Minimum fraction of smooth edges required for a surface cluster to qualify for primitive detection. Lower = more lenient shape detection.                                                                                                                                                                                                |
| `UseEdgeWalkSampling`           | bool  | `False` | Enables edge-walk sampling during hidden-tail verification. Catches pits/gaps between discrete sample points, so pits and holes in model will not beaccidentaly covered by HidingParallelogramTail. More accurate but MUCH slower.                                                                                                       |
| `HiddenTailPullIn`              | float | `0.03`  | Pull-in distance along normal for hidden-tail solid checks. Larger values produce lower primitives at the cost of model look getting "spiky".                                                                                                                                                                                            |
| `AllowNonPlanarNGons`           | bool  | `False` | When enabled, non-planar n-gons are decomposed into parallelograms as-is instead of being fan-triangulated into planar triangles first. Reduces primitive count on models with many non-planar faces at the cost of geometric inaccuracy since the resulting parallelograms won't lie perfectly on the original surface.                 |
| `PlanarThreshold`               | float | `0`     | Maximum vertex displacement when merging coplanar faces. `0` = exact only. Higher values (e.g. 0.0001 or 0.001) merge more aggressively, producing fewer larger polygons and fewer primitives, but with increasing this parameter too high, it can actually increase the amount of primitives, as it deforms the original model too much |
| `DeduplicateVertexThreshold`    | float | `1E-04` | Vertex deduplication distance. Vertices closer than this are merged. Rarely needs changing.                                                                                                                                                                                                                                              |
| `DeduplicatePlaneDistThreshold` | float | `1E-04` | Plane distance deduplication threshold for coplanar detection. Rarely needs changing.                                                                                                                                                                                                                                                    |
| `MaxMsPerFrame`                 | float | `8`     | Maximum milliseconds per frame before yielding in coroutine mode. Higher = faster loading but more lag. Lower = smoother but slower loading.                                                                                                                                                                                             |

### Tuning Guide

**To reduce primitive count** (at the cost of accuracy):

- Enable `UseHiddenTailOptimization` and `DetectPrimitives` if disabled
- Increase `Accuracy` (e.g. `0.01`) to allow more stretch reuse
- Increase `HiddenTailPullIn` (e.g. `0.1`) to get less primitives at the cost of more visible spikes on the model
- Increase `PlanarThreshold` (e.g. `0.0001` - `0.001`) to merge more coplanar faces (not that much of optimization)
- Increase `SmoothMaxAngle` (e.g. `0.5`) for more aggressive shape detection
- Enable `AllowNonPlanarNGons` to keep large n-gons intact instead of splitting them (may cause heavy geometric inaccuracy)

**To increase visual accuracy** (at the cost of more primitives):

- Set `PlanarThreshold` to `0`
- Enable `UseEdgeWalkSampling` for hidden tail optimization to catch small pits and holes in the model (warning: very slow)
- Decrease `Accuracy` (e.g. `0.0001` - should be enough for any model)
- Decrease `SmoothMaxAngle` to avoid replacing low-poly geometry with smooth shapes

**To speed up loading** (at the cost of gameplay smoothness):

- Increase `MaxMsPerFrame` (e.g. `16` or `32`)

---

## Project Structure

Each subfolder with significant logic has its own `README.md` with detailed algorithm descriptions.

```
TriangleScpSl/
  Plugin.cs, Config.cs          - EXILED plugin entry point
  Commands/                     - Remote Admin commands
  Core/
    FileToTriangles/            - Simple OBJ parser (triangle-based, legacy)
    ModelFactory/               - Factory for loading OBJ and creating models
    Models/
      ModelBase.cs              - Abstract base with shared fields and methods
      ApproximateModel/         - V2 stretch-clustering renderer
      ExactModel/               - V1 exact parallelogram renderer
    NGons/                      - N-gon decomposition pipeline
      Detectors/                - Primitive shape detection
    Triangulation/
      Triangle/                 - Triangle data and decomposition into parallelograms
      Parallelogram/            - Parallelogram data and the 2-Quad shearing primitive
    ProjectMerExport/           - ProjectMER schematic JSON export
    Paths/                      - File path utilities
    Runtime/                    - Coroutine host
```

---

## API Overview

For programmatic use, the key entry points are:

```csharp
// Load an OBJ with the full N-gon pipeline (recommended, coroutine)
NGonModelConfig config = NGonModelConfig.CreateFromSession();
yield return NGonModelBuilder.LoadCoroutine("model.obj", Color.white, result =>
{
    // result.Parallelograms, result.DetectedPrimitives, result.NormalizedFileName
}, config);

// Or synchronous (blocks the main thread)
NGonModelResult result = NGonModelBuilder.Load("model.obj", Color.white, config);

// Create models
var exact = ExactModel.Create(result.Parallelograms, result.DetectedPrimitives, position);
var approx = ApproximateModel.Create(result.Parallelograms, result.DetectedPrimitives, position);

// Or deferred (build in coroutine to avoid lag)
var model = ApproximateModel.CreateDeferred(result.Parallelograms, result.DetectedPrimitives, position);
yield return model.BuildTrianglesCoroutine(PrimitiveFlags.Visible, batchSize: 64);

// Control
model.Position = newPos;
model.Rotation = newRot;
model.Scale = Vector3.one * 2f;
model.Color = Color.red;
model.Destroy();
```

See `Core/Models/ApproximateModel/README.md` and `Core/Models/ExactModel/README.md` for full API details.

---

## Acknowledgments & Dependencies

- **[EXILED](https://github.com/ExMod-Team/EXILED)** — Creative Commons Attribution-ShareAlike 3.0 Unported
- **[Mirror Networking](https://github.com/MirrorNetworking/Mirror)** — MIT
- **[Unity Engine](https://unity.com/)** — Unity Companion License
