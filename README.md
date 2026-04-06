# Triangle

An [EXILED](https://github.com/ExMod-Team/EXILED) plugin for SCP: Secret Laboratory that renders filled triangles and STL-based 3-D meshes in world space using primitive toys.

## Current project info

- Plugin name: `Triangle`
- Version: `2.0.0`
- Author: `Foibos`
- Target framework: `net48`
- EXILED package: `ExMod.Exiled 9.13.3`

## How it works

Each triangle is rendered as three parallelograms that share the triangle area.

- `TrianglePrimitive` renders one triangle with its own invisible root quad.
- `TriangleSpace` renders many triangles with three shared invisible roots at a common origin.
- `TriangulatedModel` is a convenience wrapper that loads `StlTriangle` data into a single `TriangleSpace`.

Each parallelogram is built from two nested quads:

1. a base quad that sets position and orientation,
2. a child quad that applies the shear needed to match the parallelogram.

When a triangle is added to a `TriangleSpace`, its parallelograms are attached to the root whose forward axis best matches the triangle normal. This keeps transform inheritance stable while the whole space can still be moved or rotated efficiently.

### Primitive count

The implementation uses three shared roots per `TriangleSpace` and three parallelograms per triangle. So each `TriangulatedModel` will have `TriangleCount * 3 + 3` quads used.

## Installation

1. Build the project.
2. Copy `Triangle.dll` into `EXILED/Plugins/`.
3. If you want to use the STL command, place models in `EXILED/Plugins/StlModels/`.

## Commands

### `Triangulate`

Spawns an STL model near the player. Running the command again destroys the currently spawned model.

Usage:

```text
triangulate <stl file>
```

Behavior:

- must be used by a player
- only a file name is allowed, not a path
- `.stl` is added automatically if omitted
- the file is loaded from `EXILED/Plugins/StlModels/

### `TriangleExample`

Spawns a randomly generated example triangle on the player. Running the command again destroys the current example.

## API

### `TriangulatedModel`

Loads and displays a 3-D mesh from a list of `StlTriangle` values.

```csharp
using Triangle.Core.Triangulation.Stl;
using Triangle.Core.Triangulation.Triangle;
using UnityEngine;

var model = TriangulatedModel.Create(stlTriangles, worldPosition, Color.white);
var model2 = new TriangulatedModel(
    stlTriangles,
    worldPosition,
    Color.white,
    PrimitiveFlags.Visible,
    scale: 0.01f,
    invertWinding: true);

int triangleCount = model.Count;
int quadCount = model.QuadCount;

model.Position = new Vector3(0, 1, 0);
model.Rotation = Quaternion.Euler(0, 90f, 0);
model.Color = Color.red;
model.Flags = PrimitiveFlags.Visible | PrimitiveFlags.Collidable;
model.Destroy();
```

Members:

- `Count` — number of triangles in the model
- `QuadCount` — reported quad count for the model
- `Position` — world position of the mesh origin
- `Rotation` — world rotation of the mesh origin
- `Color` — updates the color of all triangle entries
- `Flags` — updates primitive flags of all triangle entries
- `Create(...)` — static factory method
- `Destroy()` — destroys the underlying primitives

Constructor / factory signature:

```csharp
TriangulatedModel(
    IReadOnlyList<StlTriangle> stlTriangles,
    Vector3 worldPosition,
    Color color,
    PrimitiveFlags flags = PrimitiveFlags.Visible,
    float scale = 1f,
    bool invertWinding = false)
```

### `TrianglePrimitive`

A single filled triangle with its own invisible root quad.

```csharp
using Triangle.Core.Triangulation.Triangle;
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
- `Move(delta)` — moves the triangle by a delta
- `Destroy()` — destroys all underlying primitives

Signature:

```csharp
TrianglePrimitive(Vector3 p1, Vector3 p2, Vector3 p3, Color color, PrimitiveFlags flags, Primitive? parent = null)
```

### `TriangleSpace`

A shared coordinate space for a collection of triangle entries.

```csharp
using Triangle.Core.TriangleMesh;
using UnityEngine;

var space = new TriangleSpace(origin);
var space2 = new TriangleSpace(origin, orientation);

TriangleEntry entry = space.AddTriangle(p1, p2, p3, Color.white);
space.Move(new Vector3(0, 1, 0));
space.SetTransform(newOrigin, newOrientation);
space.Destroy();
```

Members:

- `TriangleSpace(Vector3 origin)`
- `TriangleSpace(Vector3 origin, Quaternion orientation)`
- `AddTriangle(p1, p2, p3, color, flags = PrimitiveFlags.Visible)`
- `Move(delta)`
- `SetTransform(origin, orientation)`
- `Destroy()`

### `ParallelogramPrimitive`

Represents one sheared parallelogram built from two nested quads.

```csharp
using Triangle.Core.Triangulation.Parallelogram;
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
ParallelogramPrimitive(Vector3 vUp, Vector3 vLeft, Vector3 center, Color color, PrimitiveFlags flags, Primitive? parent = null)
```

## Notes

- `TriangleEntry` is an internal type used by `TriangleSpace`.
- `StlParser` skips non-finite vertices and ignores malformed triangles instead of crashing.
- `TriangulatedModel` centers imported STL geometry around the bounding-box center before applying the requested `scale` and `worldPosition`.

