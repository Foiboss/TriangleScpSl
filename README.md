# Triangle

![blender-monkey](https://github.com/user-attachments/assets/2012cb09-db5a-4140-a48f-e1e865e89234)


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
- `TriangleSpace` renders many triangles with a shared invisible base root and three axis-oriented invisible roots under it.
- `TriangulatedModel` is a convenience wrapper that loads `StlTriangle` data into a single `TriangleSpace`.

Each parallelogram is built from two nested quads:

1. a base quad that sets position and orientation,
2. a child quad that applies the shear needed to match the parallelogram.

When a triangle is added to a `TriangleSpace`, its parallelograms are attached to the root whose forward axis best matches the triangle normal. This keeps transform inheritance stable while the whole space can still be moved or rotated efficiently.

### Primitive count

The implementation uses one shared base root + three shared axis roots per `TriangleSpace` and three parallelograms per triangle. So each `TriangulatedModel` will have `TriangleCount * 3 + 4` quads used.

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
- `Position` — world position of the shared base root
- `Rotation` — world rotation of the shared base root
- `AddTriangle(p1, p2, p3, color, flags = PrimitiveFlags.Visible)`
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

## Mathematical Details: Parallelogram Construction

![derivation](https://github.com/user-attachments/assets/78faa24b-2003-45b6-b9e5-cde804db598f)

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

## Architecture Details: Why This Works

### Triangle Rendering with Quads

A single triangle is decomposed into three overlapping parallelograms that together fill the entire triangle area. Each parallelogram is rendered using **2 nested quads** with the following architecture:

```
TrianglePrimitive
├── _root (1 invisible quad at centroid)
│   ├── _prim1 (ParallelogramPrimitive)
│   │   ├── _baseQuad (invisible, defines position/orientation/shear)
│   │   └── _quad (visible, final sheared shape)
│   ├── _prim2 (ParallelogramPrimitive)
│   │   ├── _baseQuad (invisible)
│   │   └── _quad (visible)
│   └── _prim3 (ParallelogramPrimitive)
│       ├── _baseQuad (invisible)
│       └── _quad (visible)
```

**Why 2 quads per parallelogram?**

The base quad cannot directly produce a sheared parallelogram due to limitations of quad scaling. Here's why we need the nested structure:

1. **Base Quad** (`_baseQuad`):
   - Position: placed at parallelogram center
   - Rotation: aligned to face the parallelogram plane (using `vNormal`)
   - Scale: `(x, 1, 1)` — applies horizontal shear factor along one axis
   - **Invisible** — doesn't render, serves as transform anchor

2. **Child Quad** (`_quad`):
   - **Parented to** the base quad (local hierarchy)
   - Local position: origin (0, 0, 0) relative to parent
   - Local rotation: `−arctan(b/a)` — rotates to un-shear the shape
   - Local scale: `(b, a, 1)` — applies the rectangle side lengths
   - **Visible** — produces the final rendered parallelogram

**The math:**

When you scale a quad by `(x, 1, 1)` and then apply a child with `(b, a, 1)`, the combined transformation creates the sheared parallelogram. The rotation of the child by `−arctan(b/a)` compensates for the shear so the final quad edges align with the parallelogram diagonals.

### Efficient Rendering with TriangleSpace

Instead of each `TrianglePrimitive` having its own root quad, the `TriangleSpace` architecture uses **1 shared base root quad** and **3 child root quads** (one for each primary axis orientation).
Each triangle's normal is compared against the forward axes of those 3 child roots. The triangle is parented to the root whose forward axis **best aligns** with the triangle's normal.

```
TriangleSpace
├── _baseRoot (invisible model-space anchor)
│   ├── _roots[0] (invisible quad aligned to X-axis)
│   │   ├── TriangleEntry 1
│   │   │   ├── ParallelogramPrimitive 1 (1 quad)
│   │   │   ├── ParallelogramPrimitive 2 (1 quad)
│   │   │   └── ParallelogramPrimitive 3 (1 quad)
│   │   ├── TriangleEntry 2 (3 parallelograms = 3 quads)
│   │   └── ...
│   ├── _roots[1] (invisible quad aligned to Y-axis)
│   │   └── ...
│   └── _roots[2] (invisible quad aligned to Z-axis)
│       └── ...
```

### Quad Counting

The formula used in `TriangulatedModel.QuadCount`:

```csharp
public int QuadCount => Count * 3 + 4;
```

Is calculated as:
- `Count * 3`: Three **visible** quads per triangle (one for each parallelogram)
- `+ 3`: Three shared **invisible axis root** quads
- `+ 1`: One shared **invisible base root** quad

## Notes

- `TriangleEntry` is an internal type used by `TriangleSpace`.
- `StlParser` skips non-finite vertices and ignores malformed triangles instead of crashing.
- `TriangulatedModel` centers imported STL geometry around the bounding-box center before applying the requested `scale` and `worldPosition`.

