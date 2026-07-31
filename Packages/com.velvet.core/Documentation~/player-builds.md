# Player builds

What the package adds to a build, and what it costs you.

Everything under `Packages/com.velvet.core/Runtime/Resources/` is in every build of every project that
installs the package, whether the project uses it or not: a `Resources` folder has no per-asset opt-out and
no stripping pass drops one. Two things live there.

## Shaders

Three of Velvet's paints are drawn by first-party shaders rather than by UI Toolkit: the drop shadow behind
`shadow-*` / `drop-shadow-*`, the sheared silhouette a `bg-gradient-*` gets on a `skew-*` element, and the
`brightness-*` / `saturate-*` filters ([styling-filters.md](styling-filters.md) owns what those two do).

The four shader files live in `Runtime/Resources/Velvet/`. There is nothing to add to Always Included
Shaders, no shader variant collection to maintain, and no build step to run: a player resolves them exactly
as Play Mode does.

**What that costs.** All four ship whether the project uses any of the three paints or not, so deleting them
from the package is the only way to keep them out — and that takes the three paints with it.

If a shader is missing anyway — a fork that pruned the folder, an asset-bundle layout that excluded it — the
paint draws nothing rather than throwing, and names the missing shader in one warning for the run, rather
than one per element the paint was due on.

## The utility stylesheet

`Runtime/Resources/Velvet/StyleUtilities.uss` imports the bundled utilities, which is what pulls them and
their partials into a build; [setup.md](setup.md) owns what the sheet is and how to put it on a panel.
