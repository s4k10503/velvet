### Changed

- The panel Velvet creates for a `V.Portal(layer:)` or a `V.WorldSpace` now carries the project's stylesheets,
  as a DOM portal's children match every stylesheet of the document. Its root carries the stylesheets of the
  element the tree was mounted on and of that element's ancestors, less any the host's panel already holds
  such as its copied theme. A sheet that comes off there comes off the host. Velvet's utility stylesheet is
  carried only while the host does not reach it another way. For a tree mounted in an `EditorWindow`, the
  sheets carried include the editor's default control sheet on the window's root. These hosts took none of
  the project's own stylesheets before.
- That host's root takes the classes of the declaring panel's root and of the element under it, a
  `UIDocument`'s root, as a DOM portal's children match the classes of `html` and `body`. A `unity-` class
  is not taken. A class on an element between those and the mount element is not taken either, so a token
  such as `.theme-ocean { --color-primary: … }` reaches portal children from the document's root and not
  from inside the page.
- The `dark` class on that host's root comes from those two document roots, or from `VelvetTheme.IsDark`
  when the mount element or an ancestor is bound to the theme, rather than from the portal's own position. A
  portal inside a `dark` section of a light page now renders light.
- A carried `:root` rule, a document class, or the copied theme's own `.unity-ui-document__root` rule does
  not paint, move or size that host's root or move its content. The root's background, border, padding,
  margin, flex layout, opacity, display and overflow are held at their initial values inline, and its
  position, offsets and size at the values that fill its panel. A layer host's transform is held too; a
  world-space host keeps the size it was given and the transform its GameObject gives it. Custom and
  inherited properties still reach the portal's children. A project theme that pads or aligns
  `.unity-ui-document__root` no longer insets or aligns layer portals' content. A layer host under a theme
  without a document-root rule, such as the empty one an editor-hosted tree's hosts get, now fills its panel.
