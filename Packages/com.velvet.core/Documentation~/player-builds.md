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

That the four names are in Always Included Shaders while the build runs is pinned by
`BundledShaderInclusionTests`. If a shader-backed paint draws nothing in your build, the player log carries a
`Shader not found` warning naming it, and that is a bug report worth filing.

**What that costs.**

- All four shaders are compiled into every player build unless the project opts out. **Project Settings ▸
  Velvet** lists each one; untick a shader and the build leaves it out of Always Included Shaders. The choice
  is saved in the project's ProjectSettings folder, so it travels with the project. A paint whose
  shader reaches the player by no other route draws nothing there and logs the warning described at the
  end of this section; the editor still resolves the shader, so only a player shows the difference.
- The entries exist only while the build runs. `ProjectSettings/GraphicsSettings.asset` is written back
  byte for byte afterwards, so nothing lands in your diff, and an entry you had listed yourself is left
  alone. A build that dies before it can undo the injection leaves the entries on disk; the next time the
  editor starts, they are removed.
- A read-only `ProjectSettings/GraphicsSettings.asset` **fails the build** before anything is written when
  the build would have to write it, because the injection has to be undone afterwards and a write that
  cannot land would leave you the diff. Check the file out of version control and build again. A build with
  nothing to add and nothing left over to undo writes nothing there, so a project that keeps the file
  read-only can list the shaders in Always Included Shaders itself, or opt out of them, and build.
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

- The sheet is in every player build unless the project opts out under **Project Settings ▸ Velvet**, by
  unticking the holder. A player built without it has no sheet for `AttachTo` to find, so
  `VelvetStyleUtilities.Sheet` throws there, naming the opt-out; opt out only when the sheet reaches your
  panels some other way, such as the scene reference [setup.md](setup.md) describes, or not at all. Opting
  out also silences the warning `V.Mount` gives for a panel without the sheet, in the editor as well.
- The entry exists only while the build runs. `ProjectSettings/ProjectSettings.asset` is written back byte
  for byte afterwards, so nothing lands in your diff; an entry you added yourself is left alone, and the
  rest of your preloaded assets — including an empty slot — go back exactly as they were. A build that dies
  before it can undo the injection leaves the entry on disk; the next time the editor starts, it is removed.
- A `ProjectSettings/ProjectSettings.asset` that cannot be opened for writing **fails the build** before
  anything is written when the build would have to write it, for the reason given for Graphics Settings
  above. A project that preloads the holder itself, or opts out of it, and has no leftover to undo builds
  from a read-only file.
- If the injection does not take for any other reason, the build **fails** and names the holder and what its
  absence would cost, rather than producing a player in which every utility the sheet declares resolves to
  nothing. The families Velvet realises from C# hold without it, so the symptom is
  partial styling rather than none — [setup.md](setup.md) carries the command that answers it per class,
  and the warning `V.Mount` raises for a panel without the sheet.

**Why not `Resources`, given it needs no build step.** Unity documents the folder as the thing to avoid, and
it measured at more than twice the added startup. **Why not Addressables**, which is the documented
replacement: it asks the consumer to create a group and run an Addressables build, and a package cannot
assume either has happened — a first-run failure there is worse than either number here.

## Engine members read by name

Some engine members Velvet depends on have no public API, so it reaches them by name: what a panel's
focus controller still holds of an element leaving it, an element's focus pseudo-state and composite-root
flag, UI Toolkit's internal property-change event, a text input's record of an open IME composition, the
`@import`s of a stylesheet, and the cached opacity
and transition lists used by [layoutId crossfades](motion.md#shared-element-layout-animation-layoutid), and
native transition activity used by [current-value starts](motion.md#driven-channels-spring-and-bezier).
`EngineMember` declares these members. `EngineMemberRegistryTests` checks direct `GetField` / `GetProperty` / `GetMethod`
and related named-reflection calls, non-generic `Enum.Parse` / `Enum.TryParse`, literal-name delegate
creation, and fluent member selections comparing `Name` to a literal outside the registry. Whole
`nameof` arguments are allowed. The guard checks these source spellings; it does not follow aliases or
values through variables.

Managed code stripping can remove a member that only a lookup by name reaches, so the package hands the
linker a link.xml keeping those members, from an `IUnityLinkerProcessor` step; a method is kept with every
overload of its name. There is nothing to configure; the file is written under the project's Temp folder
while the build runs.

A member that no longer resolves leaves the feature reading it undone rather than throwing.
`EngineMemberResolutionTests` resolves every declaration, with its member kind and type, against the
editor running the suite, so a Unity upgrade that renames or retypes one fails there and names it.
Shape matching checks type metadata, generic arguments, vector elements and by-ref parameters rather
than a type's display string. Method declarations include their return and parameter types; focus
pseudo-state declarations also require an enum.

`ImportedStyleSheetPlayerTests` checks the `@import` lookup from a macOS IL2CPP player with High managed
stripping. It mounts a target carrying an importing USS, then a real asset-bundle copy, then a bare control, and
checks their warning phases alongside the USS-owned utility's resolved style. The bundle copy must be a
distinct object with the original sheet's name and nonempty import names. Shader lookup and support are
checked against names read from the package's shader files, without serialized shader references. Editor and in-editor PlayMode runs
skip the player-only fixture.

For a standalone scene that calls the same assertion directly, set VELVET_IMPORT_PLAYER_OUTPUT to the
output `.app` path and VELVET_IMPORT_PLAYER_EVIDENCE to a log directory, then launch the editor with
`-batchmode -debugCodeOptimization -quit -executeMethod Velvet.Tests.ImportedStyleSheetPlayerSetup.BuildProbe`.
This requires the editor's Mac IL2CPP support. The scoped builder temporarily preloads the test assets,
builds the stylesheet bundle, creates a test scene, and selects ARM64, IL2CPP and High stripping; cleanup restores those settings and removes
the scene and assets. Run the built app's executable with `-batchmode` and `--velvet-import-result` followed by an absolute
JSON output path. The probe writes its runtime platform, compiled backend, and assertion result, then
exits with status zero on success.

Set VELVET_IMPORT_PLAYER_NATIVE_ONLY=1 to use the same builder for the native-transition current-value
assertion without the shader and bundle checks. It checks idle, running and naturally completed native
activity, then takes over from a displayed intermediate width and checks the custom start and next
frame. The result includes those measurements and cleanup success.
