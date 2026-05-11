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

> ### `TriangulateNGonOpt` — the recommended command
>
> This command applies **all optimizations**: N-gon decomposition, coplanar merging, rectangle detection, primitive shape detection, hidden tail optimization, and V2 stretch clustering. It produces the lowest primitive count.
>
> ```
> TriangulateNGonOpt <model.obj> [accuracy] [smoothness]
> ```
>
> - `accuracy` (default `0.001`): max vertex error in world units. Lower = more precise, more primitives.
> - `smoothness` (default `0.32`): max dihedral angle (radians) for primitive shape detection. Controls how aggressively spheres/cylinders are detected. `0.32 rad ~ 18 degrees`.

---

## All Commands

Every command can be run again while building to cancel, or run again after the model is visible to destroy it.

### Display Commands (player only)

| Command                                             | Pipeline              | Description                                                                             |
|-----------------------------------------------------|-----------------------|-----------------------------------------------------------------------------------------|
| `TriangulateNGonOpt <file> [accuracy] [smoothness]` | N-gon + V2 + all opts | **Best result.** Lowest primitive count with all optimizations.                         |
| `TriangulateNGonV2 <file> [planar] [accuracy]`      | N-gon + V2            | N-gon decomposition with stretch clustering. No primitive shape detection.              |
| `TriangulateNGon <file> [planar]`                   | N-gon + V1            | N-gon decomposition with exact parallelograms.                                          |
| `TriangulateV2 <file> [accuracy]`                   | Triangle + V2         | Triangle-based with stretch clustering. Simpler, but higher primitive count than N-gon. |
| `Triangulate <file>`                                | Triangle + V1         | Simplest pipeline. Triangle-based, exact parallelograms. Highest primitive count.       |

### Export Commands (also works in server console)

| Command                                                          | Pipeline              | Description                                                              |
|------------------------------------------------------------------|-----------------------|--------------------------------------------------------------------------|
| `ExportSchematicNGonOpt <file> <output> [accuracy] [smoothness]` | N-gon + V2 + all opts | **Best export.** All optimizations, same pipeline as TriangulateNGonOpt. |
| `ExportSchematicNGonV2 <file> <output> [planar] [accuracy]`      | N-gon + V2            | Export to ProjectMER schematic JSON.                                     |
| `ExportSchematicNGon <file> <output> [planar]`                   | N-gon + V1            | Export to ProjectMER schematic JSON.                                     |
| `ExportSchematicV2 <file> <output> [accuracy]`                   | Triangle + V2         | Export to ProjectMER schematic JSON.                                     |
| `ExportSchematic <file> <output>`                                | Triangle + V1         | Export to ProjectMER schematic JSON.                                     |

### Debug Commands (player only)

| Command                      | Description                                                                     |
|------------------------------|---------------------------------------------------------------------------------|
| `TriangleExample`            | Spawns a random triangle with colored vertex markers.                           |
| `TestParallelograms [count]` | Visualizes V2 stretch clustering with random parallelograms.                    |
| `TestNGons <file> [stage]`   | Visualizes N-gon pipeline stages (0=raw, 1=merged, 2=convex, 3=parallelograms). |

### Parameters

- **`accuracy`** — Maximum vertex error (world units) when reusing stretch primitives. Default: `0.001`. Lower = more precise but more primitives.
- **`planar threshold`** — Maximum vertex displacement during coplanar face merging. Default: `0`. Set to `0` to disable merging; higher values merge more aggressively.
- **`smoothness`** — Maximum dihedral angle (radians) between adjacent faces for primitive shape detection. Default: `0.32` (~18 degrees). Higher values detect shapes more aggressively.

---

## Configuration

In the EXILED config file:

| Setting                            | Default | Description                                 |
|------------------------------------|---------|---------------------------------------------|
| `TriangulateBuildBatchSize`        | `128`   | Primitives per frame for Triangulate        |
| `TriangulateV2BuildBatchSize`      | `64`    | Primitives per frame for TriangulateV2      |
| `TriangulateNGonBuildBatchSize`    | `128`   | Primitives per frame for TriangulateNGon    |
| `TriangulateNGonV2BuildBatchSize`  | `64`    | Primitives per frame for TriangulateNGonV2  |
| `TriangulateNGonOptBuildBatchSize` | `128`   | Primitives per frame for TriangulateNGonOpt |
| `ExportBuildBatchSize`             | `128`   | Primitives per frame during export          |
| `ExportWriteBatchSize`             | `512`   | Blocks per write batch during export        |

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
// Load an OBJ and get parallelograms + detected primitives (recommended)
NGonModelBuilder.TryLoad("model.obj", Color.white,
    out List<ModelParallelogram> parallelograms,
    out List<ModelPrimitive> detectedPrimitives,
    out string fileName, out string error);

// Create models
var exact = ExactModel.Create(parallelograms, detectedPrimitives, position);
var approx = ApproximateModel.Create(parallelograms, detectedPrimitives, position);

// Or deferred (build in coroutine to avoid lag)
var model = ApproximateModel.CreateDeferred(parallelograms, detectedPrimitives, position);
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
