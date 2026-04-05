# Triangle

An [EXILED](https://github.com/ExMod-Team/EXILED) plugin for SCP: Secret Laboratory that renders filled triangles in 3D space using primitive toys.

> Note: this could probably also be done with 3 `Quad`s and 3 `GameObject`s, but I haven't managed to implement that version yet.

## How it works

SCP:SL's `Primitive` toy only provides a `Quad` — a flat square. A filled triangle cannot be represented by a single quad. Instead, each triangle **ABC** is split into three parallelograms, one anchored at each vertex, that tile the triangle without gaps or overlaps.

Each parallelogram is defined by a center point **C**, an "up" half-diagonal **vUp** (center → vertex), and a "left" half-diagonal **vLeft** (center → adjacent midpoint). It is drawn with two nested Unity quads: a **base quad** that sets position/orientation, and a **child quad** that applies the shear to match the actual parallelogram shape.

---

## Formula derivation

A parallelogram with half-diagonals **vUp** and **vLeft** is drawn by finding an inner rectangle of sides `a × b` and a horizontal shear factor `x` such that the result matches the shape.

**Step 1 — decompose vLeft:**

```
upLen = |vUp|
leftY = dot(vLeft, vUp) / upLen      // projection along vUp
leftX = |vLeft − leftY · vUp/upLen|  // perpendicular component
```

**Step 2 — compute inner rectangle sides:**

Let `l3 = 2·upLen` (full diagonal of the inner rectangle). Using the chord relations from the diagram (angles α = arctan(a/b) and β = arctan(b/a)):

```
l1 = b · √((xa)² + b²) / l3       // side length 1
l2 = a · √((xb)² + a²) / l3       // side length 2
l3 = √(a² + b²)
```

Key identity that makes the construction consistent: **a·cos(α) = b·cos(β) = ab/l3**

**Step 3 — invert to find a, b, x:**

From `l3² = a² + b²` and `l1² − l2² = b² − a²` one derives:

```
a² = (l3² + l2² − l1²) / 2 = 2·upLen·(upLen + leftY)
b² = (l3² + l1² − l2²) / 2 = 2·upLen·(upLen − leftY)
x  = leftX · l3 / (a·b)
```

Conditions `l1 < l3` and `l2 < l3` are satisfied whenever the triangle is non-degenerate.

**Step 4 — apply the transform:**

The base quad is placed at `center`, rotated to face the plane of the parallelogram, and scaled by `x` along the base axis. The child quad is attached with a local rotation of `−arctan(b/a)` and scale `(b, a, 1)` to produce the final sheared shape.

---

If you want to inspect the mathematical derivation, look at the image below.

<details>
 <summary>Show the image</summary>
  
  ![math-derivation](https://github.com/user-attachments/assets/e42a025b-9b0d-45b8-bedc-fd0c52cb8433)

</details>

---

## Installation

Drop `Triangle.dll` into `EXILED/Plugins/`.

---

## API

### `TrianglePrimitive`

```csharp
using AdminToys;
using Triangle.Core.Triangulation.Triangle;
using UnityEngine;

// Create
var tri = TrianglePrimitive.Create(p1, p2, p3, Color.red);
var tri = TrianglePrimitive.Create(p1, p2, p3, Color.red, PrimitiveFlags.Visible | PrimitiveFlags.Collidable);
var tri = new TrianglePrimitive(p1, p2, p3, Color.red, PrimitiveFlags.Visible);

// Read-only vertices
Vector3 v1 = tri.P1;
Vector3 v2 = tri.P2;
Vector3 v3 = tri.P3;
List<Vector3> pts = tri.GetPoints();

// Geometry
Vector3 center = tri.Center;   // centroid
Vector3 normal = tri.Normal;   // face normal (normalized)
float   area   = tri.Area;     // area in world units²
Bounds  bounds = tri.Bounds;   // axis-aligned bounding box

// Appearance
tri.Color       = Color.blue;
tri.Flags       = PrimitiveFlags.Visible | PrimitiveFlags.Collidable;

// Transform
tri.Rebuild(newP1, newP2, newP3);  // move all vertices at once
tri.Move(new Vector3(0, 1, 0));    // translate by delta

// Cleanup
tri.Destroy();
```

### `TrianglePrimitive` members

- `P1`, `P2`, `P3` — the triangle vertices.
- `Center` — centroid of the triangle.
- `Normal` — normalized face normal.
- `Area` — triangle area.
- `Bounds` — axis-aligned bounding box.
- `Color` — updates the color of all three parallelograms.
- `Flags` — updates primitive flags for all three parallelograms.
- `GetPoints()` — returns the three vertices as a list.
- `Rebuild(p1, p2, p3)` — rebuilds the triangle from new vertices.
- `Move(delta)` — translates the triangle by a delta.
- `Destroy()` — destroys all underlying primitives.

### `ParallelogramPrimitive`

```csharp
using Triangle.Core.Triangulation.Parallelogram;

// Create (vUp and vLeft are half-diagonals from center)
var para = ParallelogramPrimitive.Create(vUp, vLeft, center, Color.green);
var para = new ParallelogramPrimitive(vUp, vLeft, center, Color.green, PrimitiveFlags.Visible);

// Geometry
Vector3      center = para.Center;
List<Vector3> pts   = para.GetPoints(); // [P1, P2, P3, P4]

// Appearance
para.Color       = Color.white;
para.Flags       = PrimitiveFlags.Visible;

// Transform
para.Rebuild(newVUp, newVLeft, newCenter);

// Cleanup
para.Destroy();
```

### `ParallelogramPrimitive` members

- `Center` — center point of the parallelogram.
- `Color` — updates the rendered color.
- `Flags` — updates primitive flags.
- `GetPoints()` — returns the four corner points.
- `Rebuild(vUp, vLeft, center)` — rebuilds the shape.
- `Destroy()` — destroys the underlying quads.

