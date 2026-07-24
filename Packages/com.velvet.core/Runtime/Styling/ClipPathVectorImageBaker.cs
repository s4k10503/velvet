#nullable enable
using UnityEngine;
using UnityEngine.UIElements;

namespace Velvet
{
    // Bakes a ClipPathSpec into a runtime VectorImage at a concrete element size. This is the
    // clip-path parity mechanism UI Toolkit (6000.3) actually supports: USS has no clip-path/mask, and
    // a live per-element custom material (style.unityMaterial, the 6.3 UI Shader Graph path) is both
    // URP-only and subject to the draw-command-order freeze that already forced DropShadowBaker off
    // it. Instead, an element with `overflow: hidden` whose background-image is a VECTOR image is
    // stencil-clipped by UITK to the vector geometry (UIR's RequiresStencilMask path — the officially
    // documented arbitrary-shape mask). Velvet wraps the clipped element in a container carrying this
    // baked VectorImage as its background, so the element's own background, borders, text and children
    // are all clipped to the shape — matching CSS clip-path's "clips everything, including descendants"
    // semantics. The path is rebuilt (re-baked) when the element's laid-out size changes — except for
    // stretch-invariant (all-percentage) shapes, whose existing bake the geometry sync rescales via
    // background-size — so percentage-based shapes track the box exactly instead of being distorted.
    internal static class ClipPathVectorImageBaker
    {
        // The shape fill drawn by the masking wrapper. The mask comes from the vector GEOMETRY
        // (stencil coverage), not its alpha, so the fill only needs to exist — but a fully
        // transparent fill risks the tessellator culling the geometry the stencil is generated
        // from, so the smallest non-zero alpha is used. White at alpha 1/255 over at most one
        // wrapper-sized quad is imperceptible.
        internal static readonly Color StencilFillColor = new(1f, 1f, 1f, 1f / 255f);

        // Cubic-bezier circle approximation constant (4/3 * (sqrt(2) - 1)).
        private const float Kappa = 0.5522848f;

        // Bakes the shape for an element laid out at (width, height) px. Returns null for a
        // degenerate (empty) shape — zero-area polygon, zero radius, inset edges that meet or cross
        // (CSS proportionally reduces over-100% inset offsets to a zero-area box) — which per CSS
        // clips EVERYTHING: the caller responds by hiding the subtree, not by dropping the mask.
        // bounds is the analytic bounding box of the path in element-local px: SaveToVectorImage
        // stores TIGHT bounds (vertices offset by the bbox min, size = bbox size), so the caller
        // must position/size the background by this rect for the shape to land where the CSS says.
        public static VectorImage? Bake(ClipPathSpec? spec, float width, float height, out Rect bounds)
        {
            bounds = default;
            if (spec == null || width <= 0f || height <= 0f || !TryComputeBounds(spec, width, height, out bounds))
            {
                return null;
            }

            using var painter = new Painter2D();
            painter.fillColor = StencilFillColor;
            painter.BeginPath();

            switch (spec.Kind)
            {
                case ClipPathKind.Polygon:
                    BuildPolygonPath(painter, spec, width, height);
                    break;
                case ClipPathKind.Circle:
                case ClipPathKind.Ellipse:
                {
                    ResolveRadialGeometry(spec, width, height, out var cx, out var cy, out var rx, out var ry);
                    BuildEllipsePath(painter, cx, cy, rx, ry);
                    break;
                }
                case ClipPathKind.Inset:
                {
                    ResolveInsetBox(spec, width, height, out var left, out var top, out var right, out var bottom);
                    BuildInsetPath(painter, spec, left, top, right, bottom);
                    break;
                }
                default:
                    return null;
            }

            painter.ClosePath();
            painter.Fill(spec.FillRule);

            var image = ScriptableObject.CreateInstance<VectorImage>();
            image.name = "VelvetClipPathBaked";
            if (!painter.SaveToVectorImage(image))
            {
                DestroyImage(image);
                return null;
            }
            return image;
        }

        // The analytic path bounding box for the shape at (width, height) px, without tessellating.
        // False for a degenerate (empty) shape — the same validity rule Bake applies, so the
        // stretch-invariant fast path in the geometry sync agrees with the full bake about when a
        // shape collapses. Cheap enough to run per geometry change.
        internal static bool TryComputeBounds(ClipPathSpec? spec, float width, float height, out Rect bounds)
        {
            bounds = default;
            if (spec == null || width <= 0f || height <= 0f)
            {
                return false;
            }

            switch (spec.Kind)
            {
                case ClipPathKind.Polygon:
                {
                    var points = spec.PolygonPoints;
                    if (points == null)
                    {
                        return false;
                    }
                    var count = points.Length / 2;
                    if (count < 3)
                    {
                        return false;
                    }
                    float minX = float.MaxValue, minY = float.MaxValue;
                    float maxX = float.MinValue, maxY = float.MinValue;
                    for (var i = 0; i < count; i++)
                    {
                        var x = points[i * 2].Resolve(width);
                        var y = points[(i * 2) + 1].Resolve(height);
                        minX = Mathf.Min(minX, x);
                        minY = Mathf.Min(minY, y);
                        maxX = Mathf.Max(maxX, x);
                        maxY = Mathf.Max(maxY, y);
                    }
                    if (maxX - minX <= 0f || maxY - minY <= 0f)
                    {
                        return false;
                    }
                    bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
                    return true;
                }
                case ClipPathKind.Circle:
                case ClipPathKind.Ellipse:
                {
                    ResolveRadialGeometry(spec, width, height, out var cx, out var cy, out var rx, out var ry);
                    if (rx <= 0f || ry <= 0f)
                    {
                        return false;
                    }
                    bounds = new Rect(cx - rx, cy - ry, rx * 2f, ry * 2f);
                    return true;
                }
                case ClipPathKind.Inset:
                {
                    ResolveInsetBox(spec, width, height, out var left, out var top, out var right, out var bottom);
                    if (right - left <= 0f || bottom - top <= 0f)
                    {
                        return false;
                    }
                    bounds = new Rect(left, top, right - left, bottom - top);
                    return true;
                }
                default:
                    return false;
            }
        }

        // A circle is an ellipse with rx == ry; its single radius lives in the spec's X slot.
        private static void ResolveRadialGeometry(ClipPathSpec spec, float width, float height,
            out float cx, out float cy, out float rx, out float ry)
        {
            cx = spec.CenterX.Resolve(width);
            cy = spec.CenterY.Resolve(height);

            if (spec.Kind == ClipPathKind.Circle)
            {
                // CSS: circle() % radius resolves against sqrt(w² + h²) / sqrt(2); the side keywords
                // measure from the center to the nearest / farthest edge.
                var centerX = cx;
                var centerY = cy;
                rx = spec.RadiusXExtent switch
                {
                    ClipPathExtent.ClosestSide => Mathf.Min(
                        Mathf.Min(centerX, width - centerX), Mathf.Min(centerY, height - centerY)),
                    ClipPathExtent.FarthestSide => Mathf.Max(
                        Mathf.Max(centerX, width - centerX), Mathf.Max(centerY, height - centerY)),
                    _ => spec.RadiusX.IsPercent
                        ? spec.RadiusX.Resolve(Mathf.Sqrt((width * width) + (height * height)) / 1.4142135f)
                        : spec.RadiusX.Resolve(0f),
                };
                ry = rx;
                return;
            }

            // CSS: ellipse() radii resolve per axis (% of width for rx, % of height for ry).
            var ecx = cx;
            var ecy = cy;
            rx = spec.RadiusXExtent switch
            {
                ClipPathExtent.ClosestSide => Mathf.Min(ecx, width - ecx),
                ClipPathExtent.FarthestSide => Mathf.Max(ecx, width - ecx),
                _ => spec.RadiusX.Resolve(width),
            };
            ry = spec.RadiusYExtent switch
            {
                ClipPathExtent.ClosestSide => Mathf.Min(ecy, height - ecy),
                ClipPathExtent.FarthestSide => Mathf.Max(ecy, height - ecy),
                _ => spec.RadiusY.Resolve(height),
            };
        }

        private static void ResolveInsetBox(ClipPathSpec spec, float width, float height,
            out float left, out float top, out float right, out float bottom)
        {
            left = spec.InsetLeft.Resolve(width);
            top = spec.InsetTop.Resolve(height);
            right = width - spec.InsetRight.Resolve(width);
            bottom = height - spec.InsetBottom.Resolve(height);
        }

        private static void BuildPolygonPath(Painter2D painter, ClipPathSpec spec, float width, float height)
        {
            var points = spec.PolygonPoints;
            if (points == null)
            {
                return;
            }
            var count = points.Length / 2;
            for (var i = 0; i < count; i++)
            {
                var p = new Vector2(points[i * 2].Resolve(width), points[(i * 2) + 1].Resolve(height));
                if (i == 0)
                {
                    painter.MoveTo(p);
                }
                else
                {
                    painter.LineTo(p);
                }
            }
        }

        // circle() and ellipse() share the bezier path: a circle is an ellipse with rx == ry. Drawn
        // as 4 cubic beziers rather than Painter2D.Arc so the ellipse case needs no scaling trick.
        private static void BuildEllipsePath(Painter2D painter, float cx, float cy, float rx, float ry)
        {
            var kx = Kappa * rx;
            var ky = Kappa * ry;
            painter.MoveTo(new Vector2(cx + rx, cy));
            painter.BezierCurveTo(new Vector2(cx + rx, cy + ky), new Vector2(cx + kx, cy + ry), new Vector2(cx, cy + ry));
            painter.BezierCurveTo(new Vector2(cx - kx, cy + ry), new Vector2(cx - rx, cy + ky), new Vector2(cx - rx, cy));
            painter.BezierCurveTo(new Vector2(cx - rx, cy - ky), new Vector2(cx - kx, cy - ry), new Vector2(cx, cy - ry));
            painter.BezierCurveTo(new Vector2(cx + kx, cy - ry), new Vector2(cx + rx, cy - ky), new Vector2(cx + rx, cy));
        }

        private static void BuildInsetPath(Painter2D painter, ClipPathSpec spec,
            float left, float top, float right, float bottom)
        {
            var boxW = right - left;
            var boxH = bottom - top;

            float tl = 0f, tr = 0f, br = 0f, bl = 0f;
            if (spec.CornerRadii != null)
            {
                // Corners are drawn as circular arcs, so a % radius resolves against the shorter box
                // axis (CSS would make it elliptical; the circular simplification keeps Painter2D's
                // Arc usable and matches Velvet's uniform-radius rounded-* model).
                var radiusBasis = Mathf.Min(boxW, boxH);
                tl = Mathf.Max(0f, spec.CornerRadii[0].Resolve(radiusBasis));
                tr = Mathf.Max(0f, spec.CornerRadii[1].Resolve(radiusBasis));
                br = Mathf.Max(0f, spec.CornerRadii[2].Resolve(radiusBasis));
                bl = Mathf.Max(0f, spec.CornerRadii[3].Resolve(radiusBasis));

                // CSS overlap rule: when adjacent radii would overlap on a side, ALL radii scale
                // down by the worst side's ratio, preserving the corner proportions.
                var f = 1f;
                ReduceRadiusScale(ref f, boxW, tl, tr);
                ReduceRadiusScale(ref f, boxW, bl, br);
                ReduceRadiusScale(ref f, boxH, tl, bl);
                ReduceRadiusScale(ref f, boxH, tr, br);
                tl *= f;
                tr *= f;
                br *= f;
                bl *= f;
            }

            // Canvas-convention rounded rect (y-down, angles clockwise from +x): each corner is a
            // quarter arc; Arc draws the connecting line from the current point automatically.
            painter.MoveTo(new Vector2(left + tl, top));
            painter.LineTo(new Vector2(right - tr, top));
            AddCorner(painter, new Vector2(right - tr, top + tr), tr, 270f, 360f, new Vector2(right, top + tr));
            painter.LineTo(new Vector2(right, bottom - br));
            AddCorner(painter, new Vector2(right - br, bottom - br), br, 0f, 90f, new Vector2(right - br, bottom));
            painter.LineTo(new Vector2(left + bl, bottom));
            AddCorner(painter, new Vector2(left + bl, bottom - bl), bl, 90f, 180f, new Vector2(left, bottom - bl));
            painter.LineTo(new Vector2(left, top + tl));
            AddCorner(painter, new Vector2(left + tl, top + tl), tl, 180f, 270f, new Vector2(left + tl, top));
        }

        private static void ReduceRadiusScale(ref float f, float side, float ra, float rb)
        {
            var sum = ra + rb;
            if (sum > side && sum > 0f)
            {
                f = Mathf.Min(f, side / sum);
            }
        }

        // A zero-radius "arc" degenerates to the corner point itself; Arc with radius 0 is skipped
        // and replaced by a straight line to where the arc would have ended.
        private static void AddCorner(Painter2D painter, Vector2 center, float radius,
            float startDeg, float endDeg, Vector2 fallbackEnd)
        {
            if (radius > 0f)
            {
                painter.Arc(center, radius, Angle.Degrees(startDeg), Angle.Degrees(endDeg));
            }
            else
            {
                painter.LineTo(fallbackEnd);
            }
        }

        // Destroys a baked VectorImage (a ScriptableObject), Play/Edit-mode aware.
        internal static void DestroyImage(VectorImage image) => VelvetObjectUtil.Destroy(image);
    }

    // Reconciler-side bookkeeping for one clipped element, keyed in
    // ReconcilerContext.ClipPathBindings by the INNER (real) element. Holds the structural
    // wrapper (the overflow-hidden stencil mask host, also registered in WrapperToInnerMap), the
    // currently-applied spec (diffed by Source on patch), the geometry callback registered
    // on the inner (so it can be unregistered on unwrap), and the live baked VectorImage —
    // a ScriptableObject that must be destroyed on re-bake and teardown.
    internal sealed class ClipPathBinding
    {
        public readonly VisualElement Wrapper;
        public ClipPathSpec? Spec;
        public EventCallback<GeometryChangedEvent> OnGeometry = null!;
        public VectorImage? Image;

        // Analytic path bounds of the live bake (element-local px). The geometry sync re-anchors the
        // background by these when only the inner's origin moved (no re-bake), and rescales them for
        // stretch-invariant shapes on a size change.
        public Rect Bounds;

        // Size of the last bake; the geometry sync skips re-baking when the box is unchanged
        // (sub-half-pixel layout jitter does not re-tessellate). -1 forces the next sync to bake.
        public float BakedWidth = -1f;
        public float BakedHeight = -1f;

        // Per-binding bake cache, keyed by SHAPE (spec.Source) — one image per distinct shape, holding the bake
        // at the most-recent size. A state variant (hover:clip-*) toggles between this element's base and hover
        // SHAPES at the same size, so each is cached and the toggle is an O(1) lookup with NO re-tessellation
        // (the whole reason clip-path state variants are viable). Keying by shape (not by shape+size) BOUNDS the
        // cache to the element's few distinct shapes: a size animation of a non-stretch-invariant shape re-bakes
        // the SAME source and destroys the stale-size image rather than accumulating one per size. Owned by THIS
        // binding (no cross-element sharing / refcounting): destroyed wholesale on unwrap / teardown.
        private System.Collections.Generic.Dictionary<string, (int W, int H, VectorImage Image, Rect Bounds)>? _bakeCache;

        public ClipPathBinding(VisualElement wrapper)
        {
            Wrapper = wrapper;
        }

        // Returns the cached bake for this shape at (w, h) or bakes a fresh one. width/height are quantized to
        // whole px (matching the geometry sync's 0.5px skip tolerance) so sub-pixel jitter does not re-bake. A
        // size change for the same shape destroys the stale-size image (cache stays one-per-shape). Returns
        // false only when the bake itself fails (degenerate/empty shape — the caller hides the subtree).
        internal bool GetOrBake(ClipPathSpec spec, float width, float height, out VectorImage? image, out Rect bounds)
        {
            var w = Mathf.RoundToInt(width);
            var h = Mathf.RoundToInt(height);
            _bakeCache ??= new System.Collections.Generic.Dictionary<string, (int, int, VectorImage, Rect)>();
            if (_bakeCache.TryGetValue(spec.Source!, out var hit) && hit.Image != null)
            {
                if (hit.W == w && hit.H == h)
                {
                    image = hit.Image;
                    bounds = hit.Bounds;
                    return true;
                }
                // Same shape, new size: the cached bake is stale — destroy it before re-baking so the cache
                // never accumulates one image per size (a resize animation would otherwise leak until teardown).
                ClipPathVectorImageBaker.DestroyImage(hit.Image);
            }
            image = ClipPathVectorImageBaker.Bake(spec, width, height, out bounds);
            if (image == null)
            {
                _bakeCache.Remove(spec.Source!);
                return false;
            }
            _bakeCache[spec.Source!] = (w, h, image, bounds);
            return true;
        }

        // Detaches the applied mask (background) WITHOUT destroying any baked image — the images live in the
        // bake cache for reuse (e.g. a hover toggle returning to a previously-baked shape). Used when the mask
        // is swapped or cleared (no active clip). Forces the next sync to re-evaluate.
        internal void DetachBackground()
        {
            Wrapper.style.backgroundImage = StyleKeyword.Null;
            Image = null;
            BakedWidth = -1f;
            BakedHeight = -1f;
        }

        // Final teardown: drops the background and destroys EVERY cached baked image. Idempotent; invoked on
        // unwrap, FiberElementCleaner teardown, and Reconciler.Dispose (EditMode has no DetachFromPanel path
        // here, so those owners must call this explicitly).
        internal void DisposeImage()
        {
            Wrapper.style.backgroundImage = StyleKeyword.Null;
            BakedWidth = -1f;
            BakedHeight = -1f;
            Image = null;
            if (_bakeCache != null)
            {
                foreach (var entry in _bakeCache.Values)
                {
                    if (entry.Image != null)
                    {
                        ClipPathVectorImageBaker.DestroyImage(entry.Image);
                    }
                }
                _bakeCache.Clear();
            }
        }
    }
}
