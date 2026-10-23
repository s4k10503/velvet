### Changed

- `V.FocusScope` renders no element around its children, as React Aria's `FocusScope` renders none: the
  children stand among the parent's own children, between two undisplayed elements marking where the scope
  starts and ends. It returns a `ComponentNode` and takes only `children`, `key` and the scope's settings;
  `className`, `name`, `props`, `styles`, `refCallback`, `data`, `aria`, `events` and the `while*Class`
  arguments are gone. A styled container that is the scope sets its `FocusScope` element prop instead,
  `V.Div(className: ..., props: new FiberElementProps { FocusScope = new FocusScopeSettings(...) })`.

- An arrow or d-pad move inside a `singleTabStop` group steps to the next member (right, down) or the
  previous one (left, up) in hierarchy order, as React Aria's toolbar and radio group do, instead of
  following the engine's on-screen geometry. An element outside the group lying between two members no
  longer stops the move, and past the group's last member focus stays where it is rather than taking the
  engine's wrap round to the group's first; `wrap: true` asks for that wrap.

- `V.Portal(layer:)` and `V.WorldSpace` take `PanelFocusOrder.Appended` by default, so their elements follow
  the declaring tree's panel in Tab order, as a portal into `document.body` follows the rest of the document:
  Tab past that panel's last element enters the host, and Tab past the host's last element goes on. Before,
  the default was `Isolated`, where Tab wrapped within the host panel; pass `focusOrder:
  PanelFocusOrder.Isolated` to keep that.
