# Player builds

What the package adds to a build, and what it costs you.

## Shaders

Three of Velvet's paints are drawn by first-party shaders rather than by UI Toolkit: the drop shadow behind
`shadow-*` / `drop-shadow-*`, the sheared silhouette a `bg-gradient-*` gets on a `skew-*` element, and the
`brightness-*` / `saturate-*` filters ([styling-filters.md](styling-filters.md) owns what those two do).
The four shader files live in `Runtime/Styles/Shaders/` and are looked up by name from C# alone.

Unity's manual states that a build strips shader variants the scenes in it do not use, and none of these is
in a scene. So the package adds them to **Graphics Settings ▸ Always Included Shaders** in an
`IPreprocessBuildWithReport` step and removes them again in the matching post-process step. There is nothing
to install, no list to maintain and no build step to run.

**What has and has not been shown.** That the four names are in Always Included Shaders while the build runs
is pinned by `BundledShaderInclusionTests`. That they then resolve from inside a running player has **not**
been observed here: seven attempts at a standalone-player test run produced one that reported and six
that timed out with the player booted and idle, and no pixel-readback fixture can pass in a player until the
themeless test host is fixed. If a shader-backed paint draws nothing in your build, the player log carries a
`Shader not found` warning naming it, and that is a bug report worth filing.

**What that costs.**

- All four shaders are compiled into every player build of every project that installs the package, whether
  the project uses any of the three paints or not. Always Included Shaders has no per-shader opt-out; the
  only way to keep one out is to delete it from the package, which takes its paint with it.
- The entries exist only while the build runs. `ProjectSettings/GraphicsSettings.asset` is written back
  byte for byte afterwards, so nothing lands in your diff, and an entry you had listed yourself is left
  alone. A build that dies before it can undo the injection leaves the entries on disk; the next time the
  editor starts, they are removed.
- A read-only `ProjectSettings/GraphicsSettings.asset` **fails the build** before anything is written,
  because the injection has to be undone afterwards and a write that cannot land would leave you the diff.
  Check the file out of version control and build again.
- If the injection does not take for any other reason, the build **fails** and names the shaders and the
  settings file, rather than producing a player whose shader-backed paints silently draw nothing.

If a shader is missing at runtime anyway, the paint draws nothing rather than throwing, and names the
missing shader in one warning for the run rather than one per element the paint was due on.

## The utility stylesheet

`Runtime/Styles/StyleUtilities.uss` and its partials reach a build through
`Runtime/Assets/VelvetRuntimeAssets.asset`, a small holder the package adds to **PlayerSettings' preloaded
assets** in an `IPreprocessBuildWithReport` step and removes again in the matching post-process step.
[setup.md](setup.md) owns what the sheet is and how to put it on a panel.

The holder exists because the two environments answer different questions. A player has no asset database,
so the sheet has to arrive as a reference something already holds; preloading loads an object but gives no
way to look one up, so the preloaded object publishes itself as it loads. An editor resolves the sheet
through the holder too when one is loaded, and falls back to reading the file whenever it is not or its
reference no longer resolves — so no editor run can see a broken holder, which is why
`BundledStyleSheetInclusionTests` pins the reference against the sheet rather than waiting for a failure.

**What it costs, measured in built players rather than argued.** Three StandaloneOSX builds of this
repository's own sample scene, differing only in how the sheet reaches them:

| | engine startup | over the sheet-absent build |
|---|---|---|
| preloaded holder (what ships) | 0.106 / 0.105 / 0.105 s | ~21 ms |
| a `Resources` folder (what shipped before) | 0.133 / 0.131 / 0.130 s | ~46 ms |
| neither, so the sheet is absent | 0.084 / 0.084 / 0.086 s | — |

Startup is `Time.realtimeSinceStartup` read from a `[RuntimeInitializeOnLoadMethod]`, three runs each, one
to two milliseconds of spread inside each arm. What the two mechanisms cost is the third column: having the
sheet at all is ~21 ms, and the `Resources` folder was ~46 ms for the same sheet. Why the folder cost more
was not measured, and is not claimed here.

**What it costs you.**

- The sheet is in every player build of every project that installs the package, whether or not anything
  calls `AttachTo`. There is no per-project opt-out short of deleting the holder from the package.
- The entry exists only while the build runs. `ProjectSettings/ProjectSettings.asset` is written back byte
  for byte afterwards, so nothing lands in your diff; an entry you added yourself is left alone, and the
  rest of your preloaded assets — including an empty slot — go back exactly as they were. A build that dies
  before it can undo the injection leaves the entry on disk; the next time the editor starts, it is removed.
- A `ProjectSettings/ProjectSettings.asset` that cannot be opened for writing **fails the build** before
  anything is written, because the injection has to be undone afterwards and a write that cannot land would
  leave you the diff. Check the file out of version control and build again.
- If the injection does not take for any other reason, the build **fails** and names the holder and what its
  absence would cost, rather than producing a player in which every plain utility class resolves to nothing.

**Why not `Resources`, given it needs no build step.** Unity documents the folder as the thing to avoid, and
it measured at more than twice the added startup. **Why not Addressables**, which is the documented
replacement: it asks the consumer to create a group and run an Addressables build, and a package cannot
assume either has happened — a first-run failure there is worse than either number here.

