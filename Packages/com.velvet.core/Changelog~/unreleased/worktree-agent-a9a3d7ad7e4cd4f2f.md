### Added

- `VelvetPreviewPanel.Render()` draws one frame into the panel's `Texture` before it returns. It first runs
  `VelvetPreviewPanel.SettlePasses` update passes over the runtime panels drawing into the texture, each followed by
  a run of Velvet's relative-length polls on them, so the renders, effects and state updates Velvet schedules on
  those panels are drawn, and so is a length measured from their layout. Work the story resumes on a later editor or player frame is
  drawn by a `Render()` after that frame. The panels are then repainted and rendered, and an exception the frame does
  not catch reaches the caller as it was thrown. The headless screenshot capture the preview guide describes calls it
  between mounting a story and reading the texture back; a mount alone does not draw its story into the texture in its
  own frame.

### Fixed

- A `VelvetPreviewPanel` built from `PanelSettings` that clear their own panel's colour no longer clears over a
  `V.Portal(UILayer.Background)` drawn into its texture, so the texture is cleared to transparent below everything
  drawn into it, as the class describes. The panel also takes its texture's size and scale when it is built rather
  than on its first update.
