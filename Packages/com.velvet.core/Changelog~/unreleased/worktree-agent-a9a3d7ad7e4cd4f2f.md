### Added

- `VelvetPreviewPanel.Render()` draws one frame into the panel's `Texture` before it returns. It first runs
  `VelvetPreviewPanel.SettlePasses` update passes over the runtime panels drawing into the texture, each a simulated
  frame that moves the panel's clock on by `VelvetPreviewPanel.SettlePassMilliseconds`, and the clock stays moved
  afterwards. Work those panels time by their own clock advances as over that many frames, so the renders, effects
  and state updates Velvet schedules there are drawn, so is what Velvet measures from their layout, and a transition
  timed by that clock and shorter than the passes together is drawn finished. Motion that reads its mount's
  `MotionClock` rather than the panel's time moves only as far as that clock has, and work the story resumes on a
  later editor or player frame is drawn by a `Render()` after that frame. The panels are then repainted and
  rendered, and an exception the frame does not catch reaches the caller as it was thrown. The headless screenshot capture the preview guide describes calls it
  between mounting a story and reading the texture back; a mount alone does not draw its story into the texture in its
  own frame.

### Fixed

- A `VelvetPreviewPanel` built from `PanelSettings` that clear their own panel's colour no longer clears over a
  `V.Portal(UILayer.Background)` drawn into its texture, so the texture is cleared to transparent below everything
  drawn into it, as the class describes. The panel also takes its texture's size and scale when it is built rather
  than on its first update.
