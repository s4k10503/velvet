# Developer tooling: Preview window

Velvet ships an editor-time preview suite — the **Storybook + Fast-Refresh equivalent** for
Velvet UI. You declare a *story* (a named, self-contained snippet of UI) once, and the preview
window mounts it onto a real UI Toolkit panel and live-renders it **without entering Play
Mode**. Pointer / focus events flow into the mounted tree's hooks and re-render in place, and
while the window is visible the editor ticks the panel scheduler each frame, so Velvet's
coalesced re-renders drain and Motion / AnimatePresence timers fire — animation runs live.

The same story source feeds the **headless screenshot capture** path, so the set you eyeball
interactively and the set captured for visual regression never drift apart.

Open the window via **Window ▸ Velvet ▸ Preview**.

## Declaring a story — `[VelvetPreview]`

Annotate a `static` method that takes no parameters (or a single args object — see
[Controls](#controls--args)) and returns a `VNode`:

```csharp
using Velvet;

internal static class MyPreviews
{
    [VelvetPreview(Name = "Primary button", Group = "Buttons", Width = 240, Height = 80)]
    private static VNode PrimaryButton() =>
        V.Button(className: "bg-primary text-white px-4 py-2 rounded", text: "Save");
}
```

The method is invoked once per mount, so it may freely construct fresh props; the rendered
tree's own hooks then drive any subsequent updates. The annotated method must be `static`,
non-generic, return `VNode`, and take either no parameters or a single args object. An invalid
signature is skipped with a console warning rather than silently dropped, so a mistyped story
is noticed.

`[VelvetPreview]` exposes four optional properties:

| Property | Type | Default | Meaning |
|---|---|---|---|
| `Name` | `string` | the method name | Display name in the story list |
| `Group` | `string` | the declaring type's name | Grouping heading (the Storybook "title" segment) |
| `Width` | `int` | `0` (fill the window) | Preferred mount width in reference pixels |
| `Height` | `int` | `0` (fill the window) | Preferred mount height in reference pixels |

Stories are addressed by a stable `Group/Name` id, so two stories must not collide on both —
a duplicate id is reported and dropped. Stories declared in **test-runner assemblies are
excluded**, so fixture stories authored for unit tests never leak into the window or the
capture set.

A story carries no environment of its own. A method's explicit `Width`/`Height` always wins
and is shown at its real footprint; a story with no explicit size fills the canvas (and is the
one a fixed viewport can simulate — see [Viewport](#viewport)).

## Shared environment — `[VelvetPreviewSetup]`

Cross-cutting setup that several stories share — registering fonts, seeding a store, wiring a
localization resolver — belongs on a `[VelvetPreviewSetup]` method. It runs **once before any
story in its assembly mounts**, and whatever it sets up is torn down symmetrically when
previewing stops or the source is rescanned, so previewing leaves no global state behind.

The method must be `static`, parameterless, and return one of three teardown shapes:

| Return | Teardown |
|---|---|
| `IDisposable` | `Dispose()` is called on teardown |
| `Action` | the returned delegate is invoked on teardown |
| `void` | nothing to undo |

At most one setup per assembly is honored; a second is ignored with a warning so the
environment a story mounts into stays unambiguous.

A setup can also hand the host an extra stylesheet to layer over Velvet's utilities — a
project's design-token `:root` overrides or a custom theme — by setting
`VelvetStyleHints.PreviewStyleSheet`. The host attaches that sheet **after** Velvet's utility
sheet (so its later source order lets equal-specificity `:root` overrides win), consumes the
hint on the next mount, and removes the sheet it added on teardown.

### Real-world example

A typical app registers one story per screen plus a controls-driven story, all sharing one
environment that stands up the app's backend (fonts + store + API + localization) and publishes
the app's token sheet:

```csharp
internal static class AppPreviews
{
    private const string Group = "App";

    [VelvetPreviewSetup]
    private static IDisposable Environment() => new AppEnvironment();

    [VelvetPreview(Name = "Home", Group = Group)]
    private static VNode Home() => Screen(HomeScreen.Render);

    // ... one per screen ...

    // A controls-driven story: edit the args in the panel and the button re-renders live.
    [VelvetPreview(Name = "Button (Controls)", Group = Group)]
    private static VNode ButtonControls(ButtonArgs args) =>
        V.Div("app-root absolute inset-0 font-sans flex items-center justify-center",
            V.Label(AppStyle.Button.Apply("flex-row",
                ("tone", args.Tone.ToString().ToLower()),
                ("size", args.Large ? "lg" : "md")), text: args.Label));

    internal sealed class ButtonArgs
    {
        public string Label = "Submit";
        public ButtonTone Tone = ButtonTone.Primary;
        public bool Large;
    }

    internal enum ButtonTone { Primary, Glass, Gold }

    private sealed class AppEnvironment : IDisposable
    {
        private readonly AppBackendScope _backend = new();

        public AppEnvironment()
        {
            var tokens = AssetDatabase.LoadAssetAtPath<StyleSheet>(
                "Assets/YourApp/styles/Tokens.uss");
            if (tokens != null) VelvetStyleHints.PreviewStyleSheet = tokens;
        }

        public void Dispose()
        {
            VelvetStyleHints.PreviewStyleSheet = null;
            _backend.Dispose();
        }
    }
}
```

Because the screens mount through the same registry the live window uses, the headless capture
harness renders them identically — a story authored once is the single source for both.

## Addons

The window's toolbar exposes a set of addons, each mirroring a Storybook one. The view-addon
state (theme / background / zoom / outline / measure / viewport) persists in `EditorPrefs`, so
it survives remounts and domain reloads.

### Controls / Args

The **Controls** addon is the Storybook "controls" equivalent. If a story takes a single
**args object** — a class / struct / record of editable props — the window reflects that type
into a column of typed editor knobs and holds one live args instance. Editing a knob writes
back into the instance and re-renders the story with the edited args **without tearing down the
assembly environment**, so a knob edited per keystroke does not re-register fonts, re-seed the
store, or recreate the dummy API each time.

Public, writable fields and properties are turned into controls; supported member types map to
these controls:

| Member type | Control |
|---|---|
| `bool` | `Toggle` |
| `int` | `IntegerField` |
| `float` | `FloatField` |
| `string` | `TextField` |
| `enum` | `EnumField` |
| `Color` (`UnityEngine.Color`) | `ColorField` |

An unsupported member type shows a read-only note rather than crashing. The args type must be
default-constructible (a struct, or a class / record with a public parameterless constructor)
so the window can seed control state from the declared defaults; the first mount and the
capture harness both render the story at those defaults.

### Viewport

The **Viewport** addon simulates responsive widths. "Full" lets the canvas fill the stage; a
fixed preset (Mobile 375, Tablet 768, Desktop 1280 reference px) sizes the canvas to that
width and makes it a **responsive scope** (it applies the `@container` marker) so the mounted
story's `sm:`/`md:`/… breakpoints evaluate against the simulated width rather than the panel
root. See [styling-variants.md](styling-variants.md) for container queries.

Two behaviors are worth knowing:

- **Re-mount on change.** A responsive manipulator binds its width source **at attach**, so
  changing the viewport re-applies the canvas size (toggling the `@container` marker) **and
  re-mounts the story** so its descendants re-attach and resolve their width source against the
  new scope. This is the supported way to drive container-query breakpoints from a runtime
  switch.
- **A story's explicit size wins.** A story with an explicit `Width`/`Height` is always shown
  at its real footprint and is **not** treated as a responsive container — the viewport
  simulation applies only to fill-canvas stories (no explicit size).

### Theme

The **Dark** toggle drives `VelvetTheme.IsDark`, so the mounted story's `dark:` variants
re-evaluate live. The window captures the editor's (or a running game's) pre-existing theme
once and restores it on close, and only reverts if the live value still equals what it last
wrote — so toggling Dark in the preview never clobbers a concurrent writer.

### Backgrounds

The **BG** menu switches the stage backdrop (the Storybook "backgrounds" addon): **Dark** (a
near-black, the default, so light UI stays legible), **Light** (a neutral gray for dark UI),
and **Checkerboard** (a transparency grid behind the canvas).

### Zoom

The **Zoom** menu scales the canvas: **Fit** (the largest scale at which the canvas fits
inside the stage), 50%, 100%, 200%. Fit recomputes against the stage and canvas sizes as they
resize.

### Outline / Measure

The **Outline** and **Measure** toggles draw a non-interactive inspection overlay above the
mounted story (it never steals the story's pointer events, so hover / click keep working
underneath):

- **Outline** — a 1px stroke around every element in the story subtree.
- **Measure** — the box model (content + padding + margin bands, with px labels) of the
  deepest element under the pointer.

All overlay geometry is computed from each element's `worldBound`, which already composes the
zoomed canvas transform, so outlines and boxes stay aligned at any zoom level.

## Scale caveat

An `EditorWindow` panel has no `PanelSettings`, so the preview renders at raw editor-panel
scale and cannot reproduce the game's `ScaleWithScreenSize` at a fixed reference resolution.
Treat the window as a live **layout / behavior** view, not a pixel-exact one. For
scale-accurate output use the headless capture path, which sets `referenceResolution` on a real
`PanelSettings`.

## Headless screenshot capture

The same `[VelvetPreview]` registry drives a registry-based **visual-regression** capture: an
editor-only harness discovers every story via `VelvetPreviewRegistry.DiscoverStories()`,
mounts each through a `VelvetPreviewHost` (which runs the story's `[VelvetPreviewSetup]`
environment), and renders it off-screen into a `RenderTexture`, writing one PNG per story. It
runs at a real reference resolution on an instantiated `PanelSettings`, so the captured output
is scale-accurate where the live window is not. Because both paths drive off one registry, the
captured set and the interactively-previewed set stay in lock-step.

## Related tooling

Velvet also ships **DevTools** — the React DevTools equivalent — a real-time VNode-tree
inspector with time-travel through a component's state-change history, opened via **Window ▸
Velvet ▸ DevTools Inspector**. Like React DevTools it **auto-attaches**: every `V.Mount` registers
its root (and unregisters on dispose), so the running app's tree appears with no manual call. Manual
`VelvetDevToolsRegistry.Register(fiber, "Label")` remains available for surfacing a specific interior
sub-tree under a custom label.
