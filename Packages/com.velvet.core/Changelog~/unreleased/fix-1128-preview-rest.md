### Added

- `VelvetPreviewSmokeTest.Run` mounts every preview story with its default args and its setups, runs the
  effects the mount left pending and the renders they schedule, and reports each story whose build, mount or
  setup threw, or during which an exception no error boundary in the story caught was logged, as Storybook's
  test runner smoke-tests a story. **Window ▸ Velvet ▸ Run Preview Smoke Test** logs the failures, and
  `-executeMethod Velvet.Editor.Preview.VelvetPreviewSmokeTestCommand.RunAndExit` runs it in a batch-mode
  editor and exits 1 when a story fails.

- The preview window's toolbar takes the game's `PanelSettings`. With one chosen, the canvas is laid out in
  the units a runtime panel on those settings uses for the viewport's screen size, and painted at that
  panel's scale. The window used to render at the editor panel's own scale only.

### Changed

- A `[VelvetPreviewSetup]` that throws is logged as an error line naming the setup, followed by the exception
  itself as an exception entry. It used to be one error line carrying the exception's text.

### Fixed

- The preview window's **Refresh** button discovers the stories again. It re-listed the set discovered first,
  so a story an assembly loaded since then did not appear.

- The DevTools window keeps its selection on the selected fiber when another entry leaves the registry. It
  kept the selection by position, so removing an entry above it moved the selection to another tree.
