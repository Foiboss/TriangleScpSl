# TriangleScpSl

![blender-monkey](https://github.com/user-attachments/assets/2012cb09-db5a-4140-a48f-e1e865e89234)


An [EXILED](https://github.com/ExMod-Team/EXILED) plugin for SCP: Secret Laboratory that renders filled triangles and STL/OBJ-based 3-D meshes in world space using primitive toys.

## Current project info

- Plugin name: `TriangleScpSl`
- Version: `2.0.0`
- Author: `Foibos`
- Target framework: `net48`
- EXILED package: `ExMod.Exiled 9.13.3`

## Table of Contents

- [How it works](#how-it-works)
  - [Primitive count](#primitive-count)
- [Installation](#installation)
- [Commands](#commands)
  - [Triangulate](#triangulate)
  - [TriangleExample](#triangleexample)
  - [ExportSchematic](#exportschematic)
- [API](#api)
  - [ModelTriangle](#modeltriangle)
  - [TriangulatedModel](#triangulatedmodel)
  - [TrianglePrimitive](#triangleprimitive)
  - [ParallelogramPrimitive](#parallelogramprimitive)
- [Mathematical Details: Parallelogram Construction](#mathematical-details-parallelogram-construction)
  - [Step 1 — Decompose vLeft](#step-1--decompose-vleft)
  - [Step 2 — Compute Inner Rectangle Sides](#step-2--compute-inner-rectangle-sides)
  - [Step 3 — Invert to Find a, b, x](#step-3--invert-to-find-a-b-x)
  - [Step 4 — Apply the Transform](#step-4--apply-the-transform)
- [Architecture Details](#architecture-details)
  - [Quad Counting](#quad-counting)

## How it works

Each triangle is rendered as three parallelograms that share the triangle area.

- `TrianglePrimitive` renders one triangle directly from three parallelograms.
- `TriangulatedModel` is a convenience wrapper that loads `ModelTriangle` data into a list of `TrianglePrimitive` objects and rebuilds them when the model transform changes.

Each parallelogram is built from two nested quads:

1. a base quad that sets position and orientation,
2. a child quad that applies the shear needed to match the parallelogram.

When a triangle is added to a `TrianglePrimitive`, its parallelograms are built directly from the triangle vertices. `TriangulatedModel` keeps triangles in model-local coordinates and reapplies translation, rotation, scale, or winding changes by rebuilding the underlying primitives.

### Primitive count

The implementation uses three parallelograms per triangle, and each parallelogram is rendered with two quads. `TriangulatedModel` also keeps one invisible base quad as a transform anchor, so total count is `TriangleCount * 6 + 1`.

## Installation

[![Download Latest Release](https://img.shields.io/badge/download-latest%20release-brightgreen?style=for-the-badge)](https://github.com/Foiboss/TriangleScpSl/releases/latest)

1. Build the project.
2. Copy `TriangleScpSl.dll` into `EXILED/Plugins/`.
3. Place models in `EXILED/Plugins/BlenderModels/`.

## Commands

### `Triangulate`

Spawns a model near the player. Running the command again destroys the currently spawned model.

```text
triangulate <model file (.stl/.obj)> [force color (true/false)]
```

- Must be used by a player.
- Only a file name is allowed, not a path.
- `.stl` is appended automatically if the extension is omitted.
- File is loaded from `EXILED/Plugins/BlenderModels/`.
- Optional second argument forces all triangles to a single fallback color (cyan).

### `TriangleExample`

Spawns a randomly generated triangle near the player with colored vertex markers. Running the command again destroys it.

### `ExportSchematic`

Exports an STL or OBJ model as a ProjectMER schematic JSON file.

```text
exportschematic <model file (.stl/.obj)> <output json> [forceObjColor(true/false)] [previewScale]
```

- Output must be a file name only (no path), e.g. `mymodel.json`.
- The schematic is written to the LabAPI ProjectMER Schematics folder.
- `previewScale` is a positive float (default `1`).

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

Loads and displays a 3-D mesh from a list of `ModelTriangle` values.

```csharp
using AdminToys;
using TriangleScpSl.Core.TriangulatedModel;
using UnityEngine;

List<ModelTriangle> triangles = [new(p1, p2, p3, Color.white)];

var model = TriangulatedModel.Create(triangles, worldPosition);
var model2 = new TriangulatedModel(triangles, worldPosition, PrimitiveFlags.Visible, scale: 0.01f, invertWinding: true);

int triangleCount = model.Count;
int quadCount = model.QuadCount;

model.Position = new Vector3(0, 1, 0);
model.Rotation = Quaternion.Euler(0, 90f, 0);
model.Scale = Vector3.one * 2f;
model.Color = Color.red;
model.Flags = PrimitiveFlags.Visible | PrimitiveFlags.Collidable;
model.Destroy();
```

Members:

- `Count` — number of triangles in the model
- `QuadCount` — total primitive count (`Count * 6 + 1`)
- `Position` — world position of the mesh origin
- `Rotation` — world rotation of the mesh origin
- `Scale` — world scale of the mesh origin
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

## Architecture Details

### Triangle Rendering with Quads

A single triangle is decomposed into three overlapping parallelograms that together fill the entire triangle area. Each parallelogram is rendered using **2 nested quads** with the following architecture:

```
TrianglePrimitive
├── _prim1 (ParallelogramPrimitive)
│   ├── _baseQuad (invisible, defines position/orientation/shear)
│   └── _quad (visible, final sheared shape)
├── _prim2 (ParallelogramPrimitive)
│   ├── _baseQuad (invisible)
│   └── _quad (visible)
└── _prim3 (ParallelogramPrimitive)
    ├── _baseQuad (invisible)
    └── _quad (visible)
```

### Quad Counting

The formula used in `TriangulatedModel.QuadCount`:

```csharp
public int QuadCount => Count * 6 + 1;
```

Is calculated as:
- `Count * 6`: Three parallelograms per triangle, two quads per parallelogram
- `+ 1`: One invisible base quad used as model transform anchor

## License

This project is licensed under the **Creative Commons Attribution-ShareAlike 3.0 Unported (CC-BY-SA 3.0)** — see the [LICENSE](LICENSE) file for details.

> **Note:** This license is required because this project depends on [EXILED](https://github.com/ExMod-Team/EXILED), which is also licensed under CC-BY-SA 3.0. When using or distributing this work, you must comply with CC-BY-SA 3.0 terms, including attribution and share-alike requirements.

## Acknowledgments & Dependencies

This plugin relies on the following projects and libraries:

- **[EXILED](https://github.com/ExMod-Team/EXILED)** — Creative Commons Attribution-ShareAlike 3.0 Unported
- **[Mirror Networking](https://github.com/MirrorNetworking/Mirror)** — MIT License
- **[Unity Engine](https://unity.com/)** — Unity Companion License
