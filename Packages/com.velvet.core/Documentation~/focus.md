# Focus & navigation: the React Aria parity guide

Velvet's focus layer models [React Aria](https://react-spectrum.adobe.com/react-aria/)'s
FocusScope / roving-tabindex / `useFocusRing` capabilities on top of UI Toolkit's own focus
machinery — composing with the engine rather than replacing it. Two things the engine already
does well are left completely untouched: **spatial 2D navigation** (arrows / d-pad / stick moves
between focusables by their on-screen geometry, on every runtime panel, with no Velvet code
involved), and the **sequential focus ring** itself (Velvet predicts and redirects it, but the
order always comes from the engine's own ring class, so the two can never disagree).

## Focus scopes

A focus scope is a container element whose subtree carries focus-management behavior, declared
either with the `V.FocusScope` factory or by setting the `FocusScope` element prop on any
existing container (the modal Div you already render can be the scope — the factory is sugar
for when no container exists yet). Four independent knobs, mirroring React Aria's props:

- **`contain`** — Tab/Shift-Tab wrap within the subtree instead of leaving it, and a move that
  escapes anyway (a spatial d-pad flick, a pointer press outside) is snapped back inside within
  the same event flush — wherever the escape landed, including inside another scope. A press on
  empty non-focusable space clears focus to nothing first (no focus event ever lands anywhere),
  so that path re-focuses the scope on the panel's next scheduler tick instead. When two
  contained scopes are live at once, the one currently holding focus wins. The modal-dialog
  behavior.
- **`restoreFocus`** — when the scope unmounts while holding focus, focus returns to the element
  it came FROM when it first entered the scope (skipped if that element is gone or can no longer
  take focus — an unmounted origin is dropped rather than chased into pool reuse). Pair with
  `contain` for dialogs.
- **`autoFocus`** — on mount, the scope's first focusable descendant takes focus (skipped when
  focus already sits inside). Mount-once, like React's `autoFocus`: a keyed reorder physically
  re-attaches the scope and must not steal focus back, so a re-attach never re-fires it.
- **`singleTabStop`** — the whole subtree behaves as ONE Tab stop, the WAI-ARIA composite-widget
  (roving tabindex) contract: Tab from inside exits past the remaining members, and Tab entering
  from outside — in either direction — lands on the member last used (else the first). The exit
  wraps within the nearest containing scope when the group is nested in one, and a group covering
  every reachable focusable holds position (in a `Chained` host panel it exits across the panel
  boundary instead — see below). Members keep their `tabIndex`, so the engine's spatial
  navigation INSIDE the group — the arrow/d-pad story — is untouched.

A documented deviation from the web: arrows/d-pad can spatially exit a `singleTabStop` group at
its edge, because spatial navigation is geometric on runtime panels. That is gamepad-correct
behavior with no web equivalent (the web has no spatial navigation to exit by).

A deliberate engine trap to know about: setting `TabIndex` to -1 on a runtime panel removes the
element from BOTH the Tab ring AND spatial 2D navigation — it is not the web's "focusable but
not tab-reachable". That is exactly why `singleTabStop` is interception-based rather than a
hand-rolled roving-tabindex over `TabIndex` values.

## Element props

Three focus-related element props ride `FiberElementProps` alongside the existing `Focusable`:
`TabIndex` (positive values sort ahead of 0 in the sequential ring; see the -1 trap above),
`DelegatesFocus` (focusing the element forwards to its first focusable child), and `FocusScope`
(the settings record behind the scope knobs above).

## Focus-visible styling and state

The `focus-visible:` class variant already covers keyboard/gamepad-only focus styling — it
lights for focus NOT caused by a pointer press on the element (keyboard, gamepad navigation, or
programmatic focus) and stays dark for click-to-focus, mirroring CSS `:focus-visible`. Nothing
new is needed for the styling channel.

`Hooks.UseFocusRing` is the render-state channel for the same distinction — React Aria's
`useFocusRing` parity: it returns the element's `IsFocused` / `IsFocusVisible` as re-rendering
component state plus a `Ref` to pass as the element's `refCallback:`. Reach for it when the
component must render differently (say, a "press A to select" hint), not just restyle; it rides
the same element-local heuristic as the `focus-visible:` variant, so the two surfaces cannot
drift apart.

## Cross-panel Tab order (`PanelFocusOrder`)

A `V.Portal(layer:)` / `V.WorldSpace` host panel owns its own focus ring, and by default that
ring is **`Isolated`**: Tab wraps within the host panel and never crosses the boundary — the
explicit-opt-in ruling from the cross-panel input-routing work, unchanged. Passing
`focusOrder: PanelFocusOrder.Chained` opts the host into the declaring panel's Tab order at the
portal's call site, with iframe semantics: tabbing through the declaring panel enters the host
when the ring reaches the portal's position (landing on the host's first focusable; Shift-Tab
enters at its last), and tabbing past the host's own last element exits back to the declaring
panel element after the call site. Arrow/2D navigation never crosses panels, matching every web
precedent for boundary crossing being sequential-only.

The escape hop is deferred by one tick of the target panel's scheduler: a synchronous
cross-panel focus handoff from inside another panel's event dispatch does not stick (verified
empirically — the still-focused source panel wins the reconciliation), so the source element is
blurred synchronously and the target focused on its own panel's next tick.

## Scope cuts

Recorded deviations/cuts, each deliberate: no imperative focus-manager handle (the declarative
scopes cover the pillars; add-on surface when a concrete consumer appears); no
`whileFocusVisibleClass` gesture prop (the `focus-visible:` variant and `UseFocusRing` cover
both channels); no orientation/wrap options on `singleTabStop` (spatial navigation already
handles in-group movement); no callback-shaped escape hook (call-site `Chained` has the web
parity anchors; a resolver hook has none and is left as a recorded follow-up); no 2D
cross-panel escape; no global input-modality tracker (the element-local focus-visible heuristic
stands — a programmatic focus right after pointer use shows the ring, which is arguably right
for gamepad UX); and cross-panel containment is not attempted (containment is per panel — a
globally exclusive modal is the Topmost layer + full-screen scrim pattern, which makes outside
input land in the modal's own panel physically).
