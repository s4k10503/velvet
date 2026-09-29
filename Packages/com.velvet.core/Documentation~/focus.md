# Focus & navigation: the React Aria parity guide

Velvet's focus layer models [React Aria](https://react-spectrum.adobe.com/react-aria/)'s
FocusScope / roving-tabindex / `useFocusRing` capabilities on top of UI Toolkit's own focus
machinery. Two things stay entirely the engine's: **spatial 2D navigation** (arrows / d-pad /
stick move between focusables by their on-screen geometry, on every runtime panel, with no
Velvet code involved), and the **sequential focus ring order** — Velvet predicts and redirects
the ring, but the order itself always comes from the engine's own ring class.

## Focus scopes

A focus scope is a container element whose subtree carries focus-management behavior, declared
either with the `V.FocusScope` factory or by setting the `FocusScope` element prop on any
existing container. Four independent knobs, mirroring React Aria's props:

- **`contain`** — Tab/Shift-Tab wrap within the subtree instead of leaving it, and a move that
  escapes anyway (a spatial d-pad flick, a pointer press outside) is snapped back inside within
  the same event flush, inside a plain scope as anywhere else. A press on empty non-focusable space
  clears focus to nothing first (no focus event ever lands anywhere), so that path re-focuses the
  scope on the panel's next scheduler tick. Focus that moves to another panel — a layer or
  world-space host, another mounted tree's panel, or a panel Velvet does not manage — is pulled back
  on that same tick. Neither pull-back applies to content of a portal declared inside the scope, in
  whatever panel the portal renders, an element-valued `V.Portal(target:)` included. When two contained
  scopes are live at once — in one panel, across panels, or across mounted trees — the one created
  later wins: a landing in it stands, and a landing in the older one is pulled back. A scope nested
  in another is created before the scope around it when both mount in the same render.
- **`restoreFocus`** — when the scope unmounts while holding focus, focus returns to the element
  it came FROM when it first entered the scope, skipped if that element is gone or can no longer
  take focus (an unmounted origin is dropped rather than chased into pool reuse). Pair with
  `contain` for dialogs.
- **`autoFocus`** — on mount, the scope's first focusable descendant takes focus (skipped when
  focus already sits inside). Mount-once, like React's `autoFocus`: a reorder that moves the scope's
  element physically re-attaches it and must not steal focus back, so a re-attach never re-fires it.
  A reorder that rebuilds the element is a fresh mount and does fire it, taking focus back from
  wherever the user had moved it — [react-migration.md, what a position is](react-migration.md#what-a-position-is)
  states which reorders rebuild, and what to write instead.
- **`singleTabStop`** — the whole subtree behaves as ONE Tab stop, the WAI-ARIA composite-widget
  (roving tabindex) contract: Tab from inside exits past the remaining members, and Tab entering
  from outside — in either direction — lands on the member last used (else the first). The exit
  wraps within the nearest containing scope when the group is nested in one, and a group covering
  every reachable focusable holds position (in a `Chained` host panel it exits across the panel
  boundary instead — see below). Members keep their `tabIndex`, so spatial navigation INSIDE the
  group is untouched.

Arrows/d-pad move between a group's members by their on-screen geometry and never leave the group:
a spatial move that lands outside it returns to the member it started from. An element outside the
group that lies between two members is where a move toward the members beyond it lands, so that
move is reverted and those members are not reached by arrows from that side.

`TabIndex` -1 takes an element out of the Tab ring while `Focus()` and a pointer press still focus
it, as the web's `tabindex="-1"` does. On a runtime panel it also takes the element out of
arrow/d-pad navigation, which is why `singleTabStop` is interception-based rather than a hand-rolled
roving tabindex over `TabIndex` values.

## Element props

Three focus-related element props ride `FiberElementProps` alongside the existing `Focusable`:
`TabIndex` (positive values sort ahead of 0 in the sequential ring; -1 is covered above),
`DelegatesFocus` (focusing the element forwards to its first focusable child), and `FocusScope`
(the settings record behind the scope knobs above).

`Focusable`, `TabIndex` and `DelegatesFocus` are restored rather than coalesced when a later render
stops declaring one: dropping the prop hands the element back the value it was constructed with, so a
`V.Div` that carried `Focusable = true` for one render stops being a Tab stop again, and a control
that is focusable by construction keeps its own default. The same holds for the other two, and it has
to: a `Label` reached through `V.Custom` is built out of the tab ring, while a `TextField` is built
delegating focus to the input beneath it — so a constant would hand one type another type's answer.

## Focus-visible styling and state

The `focus-visible:` class variant covers keyboard/gamepad-only focus styling, mirroring CSS
`:focus-visible` with React Aria's input modality kept per panel: after a pointer press anywhere in
the element's panel it stays dark — for click-to-focus, and for a programmatic `Focus()` that
follows the press — until a key press or a navigation move in that panel. Keyboard and gamepad
focus light it, as does a programmatic focus in a panel that has had no pointer press since its last
key press or navigation move. A Shift, Ctrl or Command key pressed alone, or a Ctrl or Command chord,
is not a key press here.

`Hooks.UseFocusRing` is the render-state channel for the same distinction — React Aria's
`useFocusRing` parity: it returns the element's `IsFocused` / `IsFocusVisible` as re-rendering
component state plus a `Ref` to pass as the element's `refCallback:`. Reach for it when the
component must render differently (say, a "press A to select" hint), not just restyle; it rides
the same heuristic as the `focus-visible:` variant.

## Cross-panel Tab order (`PanelFocusOrder`)

A `V.Portal(layer:)` / `V.WorldSpace` host panel owns its own focus ring, and by default that
ring is **`Isolated`**: Tab wraps within the host panel and never crosses the boundary. Passing
`focusOrder: PanelFocusOrder.Chained` opts the host into the declaring panel's Tab order at the
portal's call site, with iframe semantics: tabbing through the declaring panel enters the host
when the ring reaches the portal's position (landing on the host's first focusable; Shift-Tab
enters at its last), and tabbing past the host's own last element exits back to the declaring
panel element after the call site. Arrow/2D navigation never crosses panels.

The escape hop is deferred by one tick of the target panel's scheduler: a synchronous
cross-panel focus handoff from inside another panel's event dispatch does not stick (verified
empirically — the still-focused source panel wins the reconciliation), so the source element is
blurred synchronously and the target focused on its own panel's next tick.

A `focusOrder:` naming no `PanelFocusOrder` member is refused at construction: `V.Portal` and
`V.WorldSpace` each throw `ArgumentOutOfRangeException` from the call itself, naming the parameter.

## Scope cuts

- No imperative focus-manager handle.
- No `whileFocusVisibleClass` gesture prop; the `focus-visible:` variant and `UseFocusRing`
  cover both channels.
- No orientation/wrap options on `singleTabStop`; spatial navigation handles in-group movement.
