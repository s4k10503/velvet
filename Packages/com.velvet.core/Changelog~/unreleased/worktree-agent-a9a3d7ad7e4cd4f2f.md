### Added

- `VelvetPreviewPanel.Render()` draws one frame into the panel's `Texture` before it returns, and
  `VelvetPreviewPanel.SettlePasses` is how many times it first updates every runtime panel drawing into the texture,
  so a state update a story's mount effect makes is drawn. The panels are then repainted and rendered, and an
  exception the frame does not catch reaches the caller as it was thrown. The headless screenshot capture the preview
  guide describes calls it between mounting a story and reading the texture back, so a capture in Edit Mode no
  longer waits for editor frames to draw the story; a mount alone does not draw its story into the texture in its
  own frame.
