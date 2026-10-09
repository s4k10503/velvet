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
  on that same tick. Content of a portal declared inside the scope counts as inside it, in whatever
  panel the portal renders, an element-valued `V.Portal(target:)` included: focus moving there stands,
  and focus leaving it for an element outside the scope, other than one in a newer contained scope, is
  pulled back, across panels on the scope's panel's next tick. A blur to nothing from that content is
  not pulled back, as React Aria listens for one on the scope's own elements only. When two contained
  scopes are live at once — in one panel, across panels, or across mounted trees — the one created
  later wins: a landing in it stands, and a landing in the older one is pulled back. A scope nested
  in another is created before the scope around it when both mount in the same render.
- **`restoreFocus`** — when the scope unmounts while holding focus, focus returns to the element
  that held focus when the scope mounted, React Aria's `nodeToRestore`, skipped if that element is
  gone or can no longer take focus (an unmounted one is dropped rather than chased into pool reuse).
  Pair with `contain` for dialogs.
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
  group is untouched, except on an axis its `orientation:` excludes (below). Of groups nested in each
  other, the outermost one below the nearest contain scope decides, as the outermost of nested toolbars does in React Aria's `useToolbar`: Tab leaves all
  of them, arrows move across the nested ones, and entry lands on the member last used at any depth.
  A plain scope nested in a group is part of the group.

Arrows/d-pad move between a group's members by their on-screen geometry and never leave the group:
a spatial move that lands outside it returns to the member it started from. An element outside the
group that lies between two members is where a move toward the members beyond it lands, so that
move is reverted and those members are not reached by arrows from that side.

`orientation:` (`FocusScopeOrientation`, `Both` by default) names the axis a group's arrows travel, as
React Aria's `useToolbar` takes an `orientation`: with `Horizontal` an up or down move, and with `Vertical`
a left or right move, is ignored before it moves focus, even where a member lies in that direction, so no
member receives focus events for it. The move still reaches the focused element's own handlers, so a
slider in the group keeps its use of the arrow. Of nested groups, the outermost one's value decides.
`orientation:` is read only on a `singleTabStop` scope.

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
`:focus-visible` with React Aria's input modality: one reading, written by every panel Velvet renders
into. A pointer press, release or move switches it to pointer, and a key press or release or a
navigation move switches it back. Focus lights the variant unless the reading is pointer — so a
click-to-focus stays dark, and so does a programmatic `Focus()` that follows pointer use, in the same
panel or another, such as a dialog's `autoFocus` in a layer host opened by a click. While the element
holds focus, the variant follows the reading: a key press lights it and a pointer press darkens it. A
key typed into a text input other than Tab or Escape leaves it as it was. A Shift, Ctrl or Command key
pressed alone, a Ctrl or Command chord, and an Alt chord off a Mac are not key presses here, as React
Aria's `isValidKey` has it.

`Hooks.UseFocusRing` is the render-state channel for the same distinction — React Aria's
`useFocusRing` parity: it returns the element's `IsFocused` / `IsFocusVisible` as re-rendering
component state plus a `Ref` to pass as the element's `refCallback:`. Reach for it when the
component must render differently (say, a "press A to select" hint), not just restyle; it rides
the same heuristic as the `focus-visible:` variant.

## Moving focus from code (`UseFocusManager`)

`Hooks.UseFocusManager` is React Aria's `useFocusManager`: it returns a `FocusManager` for the focus
scope around the calling component — the nearest `V.FocusScope` or `FocusScope` element prop above it,
where a component inside a portal reaches the scope the portal is declared in. `FocusNext`,
`FocusPrevious`, `FocusFirst` and `FocusLast` focus an element of that scope and return it, or return
null and leave focus alone when there is none to move to or the component is in no scope.

The elements are the scope's descendants in hierarchy order. A field such as a `TextField` or a
`Toggle` is one element. A disabled element, anything
under an element that is not displayed, and an element other than a field that delegates its focus
are skipped.
`FocusManagerOptions` carries React Aria's four options: `Tabbable` skips a negative `TabIndex`,
`Accept` filters, and — for `FocusNext` and `FocusPrevious` — `Wrap` continues past the scope's end
from its other end and `From` moves from that element instead of the focused one, into its own
descendants first. When focus is outside the scope, `FocusNext` lands on the first element and
`FocusPrevious` on the last. A portal's content is not among the elements of the scope it is declared
in, as React Aria's scope walker reaches only the scope's own DOM.

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

- No `whileFocusVisibleClass` gesture prop; the `focus-visible:` variant and `UseFocusRing`
  cover both channels.
- No `wrap` option on `singleTabStop`; spatial navigation decides where an arrow at the group's edge lands.
