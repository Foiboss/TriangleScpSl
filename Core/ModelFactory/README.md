# ModelFactory (Legacy)

Static factory that loads OBJ files via the legacy triangle parser and creates models.

Used by the simple `Triangulate` / `TriangulateV2` commands. **For best results, use `NGonModelBuilder.Load()` or `NGonModelBuilder.LoadCoroutine()` instead**, which apply the full N-gon optimization pipeline.

## Files

| File              | Content                                                                                                     |
|-------------------|-------------------------------------------------------------------------------------------------------------|
| `ModelFactory.cs` | `TryLoadTriangles` (loads OBJ via `ObjParser`), `CreateModel` overloads for ExactModel and ApproximateModel |
