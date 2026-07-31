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

`Runtime/Resources/Velvet/StyleUtilities.uss` imports the bundled utilities, which is what pulls them and
their partials into a build; [setup.md](setup.md) owns what the sheet is and how to put it on a panel.
Everything under a `Resources` folder is in every build of every project that installs the package, whether
the project uses it or not.
