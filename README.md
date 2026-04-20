# TriangleScpSl

![blender-monkey](https://github.com/user-attachments/assets/2012cb09-db5a-4140-a48f-e1e865e89234)


An [EXILED](https://github.com/ExMod-Team/EXILED) plugin for SCP: Secret Laboratory that renders filled triangles and STL/OBJ-based 3-D meshes in world space using primitive toys.

## Current project info

- Plugin name: `TriangleScpSl`
- Version: `2.2.0`
- Author: `Foibos`
- Target framework: `net48`
- EXILED package: `ExMod.Exiled 9.13.3`

## Table of Contents

- [How it works](#how-it-works)
  - [V1 — Per-triangle parallelograms](#v1--per-triangle-parallelograms)
  - [V2 — Shared stretch primitives](#v2--shared-stretch-primitives)
  - [Primitive count](#primitive-count)
- [Installation](#installation)
- [Commands](#commands)
  - [Triangulate](#triangulate)
  - [TriangulateV2](#triangulatev2)
  - [TriangleExample](#triangleexample)
  - [ExportSchematic](#exportschematic)
  - [ExportSchematicV2](#exportschematicv2)
- [API](#api)
  - [ModelTriangle](#modeltriangle)
  - [TriangulatedModel](#triangulatedmodel)
  - [ParallelogramSpace](#parallelogramspace)
  - [TrianglePrimitive](#triangleprimitive)
  - [ParallelogramPrimitive](#parallelogramprimitive)
- [Mathematical Details: Parallelogram Construction](#mathematical-details-parallelogram-construction)
  - [Step 1 — Decompose vLeft](#step-1--decompose-vleft)
  - [Step 2 — Compute Inner Rectangle Sides](#step-2--compute-inner-rectangle-sides)
  - [Step 3 — Invert to Find a, b, x](#step-3--invert-to-find-a-b-x)
  - [Step 4 — Apply the Transform](#step-4--apply-the-transform)
- [V2 Mathematical Details: Stretch Clustering](#v2-mathematical-details-stretch-clustering)
- [Architecture Details](#architecture-details)
  - [V1 Architecture](#v1-architecture)
  - [V2 Architecture](#v2-architecture)
  - [Quad Counting](#quad-counting)

## How it works

Each triangle is rendered as three parallelograms that share the triangle area. Two rendering pipelines are available.

### V1 — Per-triangle parallelograms

Each parallelogram is built from two nested quads:

1. a base quad that sets position and orientation,
2. a child quad that applies the shear needed to match the parallelogram.

`TrianglePrimitive` renders one triangle directly from three `ParallelogramPrimitive` objects. `TriangulatedModel` is a convenience wrapper that loads `ModelTriangle` data into a list of `TrianglePrimitive` objects and keeps an internal transform anchor.

### V2 — Shared stretch primitives

`ParallelogramSpace` uses an improved algorithm that groups parallelograms with similar orientations together under a shared *stretch* primitive (a quad scaled along X by `cos(φ)·F` and along Y by `sin(φ)·F` and rotated by θ). Child quads placed under the same stretch are deformed uniformly by it, so the visible shape is reproduced without a separate base quad per parallelogram.

`VectorPhiSolver` finds the (θ, φ) pair for each parallelogram. `StretchSpatialIndex` clusters nearby (θ, φ) pairs into cells and reuses a single stretch primitive for all parallelograms within the configured angular tolerance. This can dramatically reduce the total quad count on models with many similarly-oriented faces.

Parallelograms that cannot be solved analytically fall back to the V1 `ParallelogramPrimitive` approach.

### Primitive count

**V1 (`TriangulatedModel`):**

```
QuadCount = TriangleCount * 6 + 1
```

- `Count * 6`: three parallelograms per triangle, two quads per parallelogram
- `+ 1`: one invisible base quad as the model transform anchor

**V2 (`ParallelogramSpace`):**

```
QuadCount = StretchCount + ParallelogramCount + FallbackCount * 2 + 1
```

- `StretchCount`: one stretch quad shared by a group of similarly-oriented parallelograms
- `ParallelogramCount`: one visible child quad per parallelogram placed under a stretch
- `FallbackCount * 2`: fallback parallelograms each use two quads (base + visible)
- `+ 1`: one invisible base quad as the model transform anchor

## Installation

[![Download Latest Release](https://img.shields.io/badge/download-latest%20release-brightgreen?style=for-the-badge)](https://github.com/Foiboss/TriangleScpSl/releases/latest)

1. Build the project.
2. Copy `TriangleScpSl.dll` into `EXILED/Plugins/`.
3. Place models in `EXILED/Plugins/BlenderModels/`.

## Commands

### `Triangulate`

Spawns a V1 model near the player. Running the command again destroys the currently spawned model.

```text
triangulate <model file (.stl/.obj)> [force color (true/false)]
```

- Must be used by a player.
- Only a file name is allowed, not a path.
- `.stl` is appended automatically if the extension is omitted.
- File is loaded from `EXILED/Plugins/BlenderModels/`.
- Optional second argument forces all triangles to a single fallback color (cyan).

### `TriangulateV2`

Spawns a V2 model near the player using shared stretch primitives. Running the command again destroys the currently spawned model.

```text
TriangulateV2 <model file (.stl/.obj)> [accuracy]
```

- Must be used by a player.
- Only a file name is allowed, not a path.
- `.stl` is appended automatically if the extension is omitted.
- File is loaded from `EXILED/Plugins/BlenderModels/`.
- `accuracy`: maximum allowed vertex error in world units when reusing a stretch (default `0.001`). Lower values increase fidelity and quad count; higher values reduce quad count at the cost of precision.

### `TriangleExample`

Spawns a randomly generated triangle near the player with colored vertex markers. Running the command again destroys it.

### `ExportSchematic`

Exports an STL or OBJ model as a V1 ProjectMER schematic JSON file.

```text
exportschematic <model file (.stl/.obj)> <output json> [forceObjColor(true/false)] [previewScale]
```

- Output must be a file name only (no path), e.g. `mymodel.json`.
- The schematic is written to the LabAPI ProjectMER Schematics folder.
- `previewScale` is a positive float (default `1`).

### `ExportSchematicV2`

Exports an STL or OBJ model as a V2 ProjectMER schematic JSON file using shared stretch primitives.

```text
ExportSchematicV2 <model file (.stl/.obj)> <output json> [accuracy] [previewScale]
```

- Output must be a file name only (no path), e.g. `mymodel.json`.
- `.json` is appended automatically if omitted.
- The schematic is written to the LabAPI ProjectMER Schematics folder.
- `accuracy`: maximum allowed vertex error in world units (default `0.001`).
- `previewScale`: positive float scale applied before export (default `1`).

## API

### `ModelTriangle`

A readonly struct that stores three vertices and a color.

```csharp
using TriangleScpSl.Core.TriangulatedModel;
using UnityEngine;

var tri = new ModelTriangle(p1, p2, p3, Color.white);
Vector3 p1 = tri.P1;
Color color = tri.Color;
```

### `TriangulatedModel`

Loads and displays a 3-D mesh from a list of `ModelTriangle` values using the V1 pipeline.

```csharp
using AdminToys;
using TriangleScpSl.Core.TriangulatedModel;
using UnityEngine;

List<ModelTriangle> triangles = [new(p1, p2, p3, Color.white)];

var model = TriangulatedModel.Create(triangles, worldPosition);
var model2 = new TriangulatedModel(triangles, worldPosition, PrimitiveFlags.Visible, scale: 0.01f, invertWinding: true);

int triangleCount = model.Count;
int quadCount = model.QuadCount; // Count * 6 + 1

model.Position = new Vector3(0, 1, 0);
model.Rotation = Quaternion.Euler(0, 90f, 0);
model.Scale = Vector3.one * 2f;

Vector3 world = model.TransformPoint(new Vector3(1, 0, 0));
Vector3 local = model.InverseTransformPoint(world);

model.Color = Color.red;
model.Flags = PrimitiveFlags.Visible | PrimitiveFlags.Collidable;
model.Destroy();
```

Members:

- `Count` — number of triangles in the model
- `QuadCount` — total primitive count (`Count * 6 + 1`)
- `Position` — position of the internal transform anchor
- `Rotation` — rotation of the internal transform anchor
- `Scale` — scale of the internal transform anchor
- `Transform` — Unity `Transform` of the internal anchor primitive
- `TransformPoint(...)` — converts a local point to world space
- `InverseTransformPoint(...)` — converts a world point to local space
- `Color` — write-only; overrides the color of all triangles
- `Flags` — write-only; sets primitive flags of all triangles
- `GetTriangleSnapshot()` — returns a snapshot as `IReadOnlyList<(ModelTriangle, PrimitiveFlags)>`
- `Create(...)` — static factory, mirrors the constructor
- `Destroy()` — destroys all underlying primitives

Constructor signature:

```csharp
TriangulatedModel(
    IReadOnlyList<ModelTriangle> triangles,
    Vector3 worldPosition,
    PrimitiveFlags flags = PrimitiveFlags.Visible,
    float scale = 1f,
    bool invertWinding = false)
```

`invertWinding` swaps the second and third vertices of every triangle, reversing the face normals. When loading files via `ModelFactory`, the winding correction for the coordinate system is applied automatically — `invertWinding` is purely for user-level control on top of that.

### `ParallelogramSpace`

Loads and displays a 3-D mesh from a list of `ModelTriangle` values using the V2 shared-stretch pipeline.

```csharp
using AdminToys;
using TriangleScpSl.ParallelogramSpace;
using UnityEngine;

List<ModelTriangle> triangles = [new(p1, p2, p3, Color.white)];

var model = ParallelogramSpace.Create(triangles, worldPosition);
var model2 = new ParallelogramSpace(
    triangles, worldPosition,
    PrimitiveFlags.Visible,
    absoluteToleranceUnits: 0.001f,
    scale: 1f,
    invertWinding: false);

int triangleCount = model.Count;
int quadCount = model.QuadCount; // StretchCount + ParallelogramCount + FallbackCount*2 + 1

model.Position = new Vector3(0, 1, 0);
model.Rotation = Quaternion.Euler(0, 90f, 0);
model.Scale = Vector3.one * 2f;

Vector3 world = model.TransformPoint(new Vector3(1, 0, 0));
Vector3 local = model.InverseTransformPoint(world);

model.Color = Color.red;
model.Flags = PrimitiveFlags.Visible | PrimitiveFlags.Collidable;

IReadOnlyList<ParallelogramSpace.ParallelogramSnapshot> paraSnap = model.GetParallelogramSnapshot();
IReadOnlyList<ParallelogramSpace.PrimitiveSnapshot> primSnap   = model.GetPrimitiveSnapshot();
IReadOnlyList<(ModelTriangle, PrimitiveFlags)>        triSnap   = model.GetTriangleSnapshot();

model.Destroy();
```

Members:

- `Count` — number of triangles in the model
- `QuadCount` — total primitive count (see [Primitive count](#primitive-count))
- `Position` — position of the internal transform anchor
- `Rotation` — rotation of the internal transform anchor
- `Scale` — scale of the internal transform anchor
- `Transform` — Unity `Transform` of the internal anchor primitive
- `TransformPoint(...)` — converts a local point to world space
- `InverseTransformPoint(...)` — converts a world point to local space
- `Color` — write-only; overrides the color of all parallelograms
- `Flags` — write-only; sets primitive flags of all parallelograms
- `GetTriangleSnapshot()` — world-space triangles as `IReadOnlyList<(ModelTriangle, PrimitiveFlags)>`
- `GetParallelogramSnapshot()` — raw parallelogram data as `IReadOnlyList<ParallelogramSnapshot>`
- `GetPrimitiveSnapshot()` — full hierarchy of all quads as `IReadOnlyList<PrimitiveSnapshot>` (used by the schematic exporter)
- `Create(...)` — static factory, mirrors the constructor
- `Destroy()` — destroys all underlying primitives

Constructor signature:

```csharp
ParallelogramSpace(
    IReadOnlyList<ModelTriangle> triangles,
    Vector3 worldPosition,
    PrimitiveFlags flags = PrimitiveFlags.Visible,
    float absoluteToleranceUnits = 0.001f,
    float scale = 1f,
    bool invertWinding = false)
```

`absoluteToleranceUnits` is the maximum allowed displacement (in world units) of any parallelogram vertex when the parallelogram is assigned to an existing stretch instead of creating a new one. Smaller values yield more stretch primitives but higher fidelity; larger values yield fewer stretch primitives with slight shape approximation.

### `TrianglePrimitive`

A single filled triangle built from three `ParallelogramPrimitive` instances.

```csharp
using AdminToys;
using TriangleScpSl.Core.Triangulation.Triangle;
using UnityEngine;

var tri = TrianglePrimitive.Create(p1, p2, p3, Color.red);
var tri2 = new TrianglePrimitive(p1, p2, p3, Color.red, PrimitiveFlags.Visible);

Vector3 center = tri.Center;
Vector3 normal = tri.Normal;
Bounds bounds = tri.Bounds;
List<Vector3> points = tri.GetPoints();

tri.Color = Color.blue;
tri.Flags = PrimitiveFlags.Visible | PrimitiveFlags.Collidable;
tri.Rebuild(newP1, newP2, newP3);
tri.Move(new Vector3(0, 1, 0));
tri.Destroy();
```

Members:

- `P1`, `P2`, `P3` — triangle vertices
- `Center` — centroid of the triangle
- `Normal` — normalized face normal
- `Bounds` — axis-aligned bounding box
- `Color` — updates the color of all three parallelograms
- `Flags` — updates primitive flags for all three parallelograms
- `GetPoints()` — returns the three vertices as a list
- `Rebuild(p1, p2, p3)` — rebuilds the triangle from new vertices
- `Move(delta)` — translates the triangle by a delta vector
- `Destroy()` — destroys all underlying primitives

Signature:

```csharp
TrianglePrimitive(Vector3 p1, Vector3 p2, Vector3 p3, Color color, PrimitiveFlags flags)
```

### `ParallelogramPrimitive`

Represents one sheared parallelogram built from two nested quads.

```csharp
using AdminToys;
using TriangleScpSl.Core.Triangulation.Parallelogram;
using UnityEngine;

var para = ParallelogramPrimitive.Create(vUp, vLeft, center, Color.green);
var para2 = new ParallelogramPrimitive(vUp, vLeft, center, Color.green, PrimitiveFlags.Visible);

Vector3 paraCenter = para.Center;
List<Vector3> pts = para.GetPoints();

para.Color = Color.white;
para.Flags = PrimitiveFlags.Visible;
para.Rebuild(newVUp, newVLeft, newCenter);
para.Destroy();
```

Members:

- `Center` — center point of the parallelogram
- `Color` — updates the rendered color
- `Flags` — updates primitive flags
- `GetPoints()` — returns the four corner points
- `Rebuild(vUp, vLeft, center)` — rebuilds the shape
- `Destroy()` — destroys the underlying quads

Signature:

```csharp
ParallelogramPrimitive(Vector3 vUp, Vector3 vLeft, Vector3 center, Color color, PrimitiveFlags flags)
```

## Mathematical Details: Parallelogram Construction

---

<details>
 <summary>Show the image explanation</summary>

![derivation](https://github.com/user-attachments/assets/78faa24b-2003-45b6-b9e5-cde804db598f)

</details>

---

A parallelogram with half-diagonals **vUp** and **vLeft** is drawn by finding an inner rectangle of sides `a × b` and a horizontal shear factor `x` such that the result matches the shape.

### Step 1 — Decompose vLeft

```
upLen = |vUp|
leftY = dot(vLeft, vUp) / upLen      // projection along vUp
leftX = |vLeft − leftY · vUp/upLen|  // perpendicular component
```

### Step 2 — Compute Inner Rectangle Sides

Let `l3 = 2·upLen` (full diagonal of the inner rectangle). Using the chord relations from the diagram (angles α = arctan(a/b) and β = arctan(b/a)):

```
l1 = b · √((xa)² + b²) / l3
l2 = a · √((xb)² + a²) / l3
l3 = √(a² + b²)
```

Key identity that makes the construction consistent: **a·cos(α) = b·cos(β) = ab/l3**

### Step 3 — Invert to Find a, b, x

From `l3² = a² + b²` and `l1² − l2² = b² − a²` one derives:

```
a² = (l3² + l2² − l1²) / 2 = 2·upLen·(upLen + leftY)
b² = (l3² + l1² − l2²) / 2 = 2·upLen·(upLen − leftY)
x  = leftX · l3 / (a·b)
```

Conditions `l1 < l3` and `l2 < l3` are satisfied whenever the triangle is non-degenerate.

### Step 4 — Apply the Transform

The base quad is placed at `center`, rotated to face the plane of the parallelogram, and scaled by `x` along the base axis. The child quad is attached with a local rotation of `−arctan(b/a)` and scale `(b, a, 1)` to produce the final sheared shape.

## V2 Mathematical Details: Stretch Clustering

The V2 pipeline replaces per-parallelogram base quads with shared *stretch* primitives. A stretch is a quad with:

- rotation `R(θ)` around Z
- scale `(cos(φ)·F, sin(φ)·F, 1)` where `F = 2`

For a parallelogram with edge vectors **v1** and **v2**, `VectorPhiSolver` finds (θ, φ) such that after applying the inverse stretch transform (rotate by −θ, then scale x by `1/(cos(φ)·F)` and y by `1/(sin(φ)·F)`), both **v1** and **v2** map to vectors of equal length. This allows the visible child quad to be placed with a simple `LookRotation` and uniform-ish scale.

`StretchSpatialIndex` maintains a 2-D spatial hash in (θ, φ) space. Before creating a new stretch, `ParallelogramSpace` queries nearby cells and measures the maximum vertex error that would result from reusing each candidate. The first candidate within `absoluteToleranceUnits` is reused; otherwise a new stretch is created.

## Architecture Details

### V1 Architecture

```
TriangulatedModel
├── _baseQuad (invisible anchor)
└── List<TrianglePrimitive>
    └── (per triangle)
        ├── ParallelogramPrimitive #1
        │   ├── _baseQuad (invisible, defines orientation/shear)
        │   └── _quad     (visible)
        ├── ParallelogramPrimitive #2
        │   ├── _baseQuad
        │   └── _quad
        └── ParallelogramPrimitive #3
            ├── _baseQuad
            └── _quad
```

### V2 Architecture

```
ParallelogramSpace
└── _baseQuad (invisible model anchor)
    ├── Stretch #1  (invisible, scale=(cos(φ)·F, sin(φ)·F, 1), rotZ=θ)
    │   ├── Parallelogram child quad A  (visible)
    │   ├── Parallelogram child quad B  (visible)
    │   └── ...
    ├── Stretch #2
    │   └── ...
    └── FallbackBase (invisible, for analytically unsolvable parallelograms)
        └── FallbackQuad (visible)
```

### Quad Counting

**V1:**

```csharp
public int QuadCount => Count * 6 + 1;
```

- `Count * 6`: three parallelograms per triangle, two quads each
- `+ 1`: invisible base quad (model anchor)

**V2:**

```csharp
public int QuadCount => _stretches.Count + _parallelograms.Count + _fallbackParallelograms.Count * 2 + 1;
```

- `_stretches.Count`: one stretch quad per unique (θ, φ) cluster
- `_parallelograms.Count`: one visible child quad per successfully solved parallelogram
- `_fallbackParallelograms.Count * 2`: two quads each for fallback parallelograms
- `+ 1`: invisible base quad (model anchor)

## License

This project is licensed under the **Creative Commons Attribution-ShareAlike 3.0 Unported (CC-BY-SA 3.0)** — see the [LICENSE](LICENSE) file for details.

> **Note:** This license is required because this project depends on [EXILED](https://github.com/ExMod-Team/EXILED), which is also licensed under CC-BY-SA 3.0. When using or distributing this work, you must comply with CC-BY-SA 3.0 terms, including attribution and share-alike requirements.

## Acknowledgments & Dependencies

This plugin relies on the following projects and libraries:

- **[EXILED](https://github.com/ExMod-Team/EXILED)** — Creative Commons Attribution-ShareAlike 3.0 Unported
- **[Mirror Networking](https://github.com/MirrorNetworking/Mirror)** — MIT License
- **[Unity Engine](https://unity.com/)** — Unity Companion License
