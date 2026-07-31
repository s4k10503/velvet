# Player builds

What the package adds to a build, and what it costs you.

## Shaders

Three of Velvet's paints are drawn by first-party shaders rather than by UI Toolkit: the drop shadow behind
`shadow-*` / `drop-shadow-*`, the sheared silhouette a `bg-gradient-*` gets on a `skew-*` element, and the
`brightness-*` / `saturate-*` filters ([styling-filters.md](styling-filters.md) owns what those two do).

The shaders live in `Packages/com.velvet.core/Runtime/Resources/Velvet/`, and Unity puts everything under a
Resources folder into every build. There is nothing to add to Always Included Shaders, no shader variant
collection to maintain, and no build step to run: a player resolves them exactly as Play Mode does.

**What that costs.** All four ship in every build of every project that installs the package, whether the
project uses any of the three paints or not. A Resources folder has no per-asset opt-out and no stripping
pass drops them, so deleting them from the package is the only way to keep them out — and that takes the
three paints with it.

If a shader is missing anyway — a fork that pruned the folder, an asset-bundle layout that excluded it — the
paint logs one warning naming the shader and then draws nothing, rather than throwing.
