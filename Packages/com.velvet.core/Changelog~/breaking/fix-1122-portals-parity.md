### Changed

- A `Button`'s `onClick` and a field's `onValueChanged` on a logical ancestor of a portal fire for a click or
  a value change raised inside that portal's children, as React's `onClick` and `onChange` bubble out of a
  portal; a disabled `Button` refuses the click, as Clickable and a disabled DOM button do. They were left to
  the physical tree, so a `V.Portal(layer:)` or a `V.WorldSpace` inside a `V.Button` never reached its
  `onClick`.

- An `events:` handler on a portal's logical ancestor fires for an event raised on an element written straight
  into the portal, as React bubbles any portal child. Only a component among the portal's children carried
  the event past the portal's boundary.

- Unscoped `sm:`…`2xl:` in a `V.Portal(layer:)`'s or a `V.WorldSpace`'s children evaluate against the width of
  the panel the portal was declared on, as a page's portals answer its one viewport; a layer host answers the
  panel of the last portal to render into it. They read the width of the host panel.

- `V.Anchored(hideWhenBehindCamera: false)` keeps following its target's projection while the target is
  behind the camera, as drei's `<Html>` does once `onOcclude` takes the hide over. It held the last position
  it had resolved.

- `V.Anchored` in an editor panel (Velvet content in an `EditorWindow`) lays the camera's viewport over the
  panel and positions itself there, as drei's `<Html>` places a projection on its canvas. It was hidden, with
  a warning.
