# FileToTriangles (Legacy)

Simple OBJ parser that triangulates all faces during parsing. This is the legacy path used by the `Triangulate` and `TriangulateV2` commands.

Unlike `NGons/ObjNGonParser` which preserves original n-gon faces, this parser converts everything to triangles immediately. This produces more parallelograms (each triangle = 3 parallelograms) but is simpler.

**For best results, use the N-gon pipeline instead** (`NGonModelBuilder.TryLoad()`).

## Files

| File           | Content                                                                                                                   |
|----------------|---------------------------------------------------------------------------------------------------------------------------|
| `ObjParser.cs` | Parses OBJ files into `ModelTriangle` lists. Handles `v`, `f` directives. Triangulates n-gon faces via fan triangulation. |
