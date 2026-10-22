### Added

- `VelvetPreviewPanel` builds a runtime panel that renders into a texture from a copy of a `PanelSettings`,
  with Velvet's utility stylesheet attached to its root, and `Resize` gives it a texture of another size. The
  preview window shows a story through one, so a capture harness that mounts a story without an explicit size
  on one built from the same settings at the window's screen size reads the window's pixels for it.
  `VelvetPreviewSmokeTest` mounts each story on one as well.

### Changed

- The preview window mounts a story on a runtime panel built from the chosen `PanelSettings` and shows the
  texture it renders, sending the pointer, wheel, keyboard and navigation events the canvas receives on to
  it. It used to mount the story on the window's own editor panel, so the editor's theme, font and scale
  applied there and the pixels it showed could differ from what a runtime panel on the game's settings
  renders. With no `PanelSettings` chosen the panel has an empty theme at a scale of one. A story's
  `sm:`…`2xl:` breakpoints read the panel, which is the simulated screen, or a story's explicit footprint
  when it has one; the window no longer marks the canvas with `VelvetResponsive.ViewportClass`. Zoom scales
  the rendered texture rather than re-rendering at the zoomed size, and Measure reports the panel's units.

### Fixed

- A `V.Portal(layer:)` declared on a panel that renders into a target texture draws into that texture, sorted
  around the panel by its layer. Its host panel used to draw on the screen, so the portal was missing from
  the texture, and from the preview window. A `VelvetPreviewPanel` clears its texture below every layer, so
  a Background portal is not erased by the panel above it.
