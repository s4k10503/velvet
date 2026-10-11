# Setup: getting the bundled utilities onto a panel

Most of Velvet's utility classes are USS rules. They live in one stylesheet the package ships,
`Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss`, and a panel that does not carry that
sheet resolves none of the classes declared in it — layout, the colour palette, spacing, sizing,
typography, borders, opacity, transforms.

A large part of the utility surface does not come from the sheet at all, which is what makes the
symptom hard to read. A utility is independent of the sheet whenever Velvet resolves it itself instead
of declaring it as a rule:

- **Arbitrary values** (`w-[120px]`, `bg-[#0000ff]`) become inline style.
- **Every family Velvet realises in C#** — among them those below. That is a sample, not the list:
  there are more, and which families sit on which side moves as utilities are added.

<!-- sheet-independent:begin — every class token between these markers is checked against the
     bundled sheets by UtilityFamilyDocumentationTests. Adding one that the sheets declare fails
     that test. It cannot tell you the sample is INCOMPLETE; see the test for why. -->

`gap-*` `space-*` `divide-*` — inter-child spacing written by a manipulator.
`ring-*` `shadow-*` `bg-gradient-to-*` — painted.
`blur-*` `brightness-*` `saturate-*` `contrast-*` `grayscale` `invert` `sepia` `hue-rotate-*`
`drop-shadow-*` — the filter set.
`animate-*` `text-balance` `outline-*` `pointer-events-*`.

<!-- sheet-independent:end -->

Because it moves, do not work from a list. The sheets are the authority, and asking them is one
command:

```bash
grep -ho '^\.[a-zA-Z0-9_-]*' Packages/com.velvet.core/Runtime/Styles/*.uss | sort -u
```

A class that prints is declared in the sheet and needs it. A class that does not carries no USS
payload of its own, and the families listed above behave identically with or without the sheet. The
text-input colour utilities, `caret-*`, `selection:bg-*` and `selection:text-*`, do not: they print
nothing, yet a field reads them only once the sheet's `.velvet-text-input` rule fires its style event,
so without the sheet they do nothing.

So a screen built from a mixture renders with the right sizes, the right gaps, a visible ring and
working filters while every palette, layout and scale class does nothing. If `flex-row` leaves a
container in a column while `gap-4` still spaces its children, the sheet is missing — not the class.

`V.Mount` says so. It looks for the sheet on the target and every ancestor, following `@import`s, so a
theme or project stylesheet that imports `StyleUtilities.uss` counts, and so does a copy of the sheet an
asset bundle carries. It looks when the target's panel next updates rather than at the call, and again
after each time the target is added to a panel, so a sheet attached after `V.Mount` but before that update
is found. If none carries it, the mount logs a warning naming the target and pointing here. It warns once
per run, however many mounts share the problem. It looks nowhere below the target, so attach the sheet to
the target or an ancestor of it. A `V.Portal` into an element you pass or register is looked at the same
way, since that element can sit on a panel of its own.

A project that leaves the sheet's holder out of its builds ([player-builds.md](player-builds.md)) gets no
warning, in the editor or in a player: leaving the holder out says the sheet reaches the panels some other
way, or is not used.

## The supported path

Attach the sheet to the element you mount onto:

```csharp
using Velvet;

VelvetStyleUtilities.AttachTo(uiDocument.rootVisualElement);
V.Mount(uiDocument.rootVisualElement, V.Component(CounterApp.Render));
```

`VelvetStyleUtilities.AttachTo` works in the editor and in a player. Attaching twice is a no-op, so a
component that owns its own panel may call it unconditionally.

Attach at the panel root unless you have a reason not to: USS matching walks up from an element
through its ancestors' sheets, so one call at the root serves the whole tree, while a sheet attached
to a subtree serves that subtree only.

`VelvetStyleUtilities.Sheet` returns the same `StyleSheet` for callers that need the asset itself —
to order it against their own sheets, or to hand it to something other than `styleSheets`. It throws
rather than returning null when the asset cannot be found, because a null sheet reaches the panel as
"nothing is styled" with no error anywhere.

### How it reaches a player, and what that costs

`AssetDatabase`, the ordinary way to reach an asset by path, exists only in the editor. In a player the
call above reads a reference instead: the package ships
`Packages/com.velvet.core/Runtime/Assets/VelvetRuntimeAssets.asset`, a holder pointing at the sheet, and a
build step adds it to PlayerSettings' preloaded assets so the build carries it. You do not configure any of
that.

The holder is in every build unless the project opts out under **Project Settings ▸ Velvet**.
[player-builds.md](player-builds.md) says what that costs, what opting out costs, and why this mechanism
rather than a `Resources` folder.

## The alternative: reference the asset from your scene

You do not have to call anything. A serialized `StyleSheet` field on a component, a
`PanelSettings` asset, or a UXML document that references
`Packages/com.velvet.core/Runtime/Styles/StyleUtilities.uss` all pull the asset into the build the
ordinary way, and you attach it from that reference. Prefer this when the sheet has to be ordered
against project sheets that are already wired through the same assets, or when your project's
convention is that no runtime code performs asset lookups.

Both put the same rules on the panel; `AttachTo` is the one that needs no scene wiring. What the
scene route does not bring with it is the theme binding `AttachTo` performs — call
`VelvetStyleUtilities.BindThemeTo(root)` as well if you want the semantic colours to follow
`VelvetTheme.IsDark`, per
[styling-variants.md](styling-variants.md#theme-the-dark-variant-and-the-token-set-beside-it).

## Where else the sheet is attached for you

Editor-time preview stories get the utilities from the preview window, so a story needs no call of its
own — see [preview-tooling.md](preview-tooling.md).

The panel Velvet creates for a `V.Portal(layer:)` or a `V.WorldSpace` gets the sheet, and your own sheets,
from the element the tree was mounted on and its ancestors, as a DOM portal's children match every
stylesheet of the document — see [portals.md](portals.md#screen-space-layers-vportallayer).

## Starter App document lifetime

The Starter App host checks the document root each frame. If the root changes, it disposes the old
mount, attaches the stylesheet to the new root and mounts the router there. Disabling the document
removes the mount; enabling it mounts again. The router keeps its location while the host remains
enabled, but component-local state starts fresh when the tree remounts.
