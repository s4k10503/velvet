### Added

- `VelvetPreviewPanel.Render()` draws one frame into the panel's `Texture` before it returns: every runtime panel
  drawing into the texture is updated, repainted and rendered. The headless screenshot capture the preview guide
  describes calls it between mounting a story and reading the texture back, so a capture in Edit Mode no longer
  depends on an editor frame passing first; a mount alone does not draw its story into the texture in its own
  frame.
