### Changed

- An args edit in the preview window, and `VelvetPreviewHost.UpdateArgs`, re-renders the mounted story with
  the new args instead of disposing it and mounting it again, so a component in the story that keeps its
  type and position keeps its state across a knob edit, as a Storybook story's does. A story that relied
  on each edit starting from fresh state now keeps what it had.

- `VelvetPreviewRegistry.DiscoverStories` throws an `InvalidOperationException` when two stories share a
  `Group/Name` id, naming each story whose id an earlier one holds together with that earlier one, and the
  preview window lists no stories and shows that message in its status line, as Storybook refuses to index
  duplicate story ids. It used to keep the first story with each id and drop the others with a warning.

- Every valid `[VelvetPreviewSetup]` an assembly declares runs for a story mount, ordered by declaring type
  and then method name, and their teardowns run in the reverse order; a teardown that throws is logged and
  the others still run. Each setup's `VelvetStyleHints.PreviewStyleSheet` is attached in setup order. Only the
  first setup found used to run, and the rest were ignored with a warning.

### Added

- An edit in the preview Controls addon hands the story a new args instance, with a copy of each nested
  object, struct, array or list on the path to the edited member, as Storybook replaces args rather than
  mutating them. The addon used to mutate one args instance in place.

- The preview Controls addon edits `long` and `double` members, an `int` or `float` carrying `[Range]` as a
  slider, a `[Flags]` enum as a flags field, a `DateTime` as text, a `UnityEngine.Object` member as an asset
  picker, and a one-dimensional array, a `List<T>` or a class or struct with public writable members as a
  foldout of controls for its elements or members, built when first expanded, with a Length field for an
  array or list, a **Set object** button for a null one, and a note where a value refers back to an object
  above it. An enum of up to five values is a radio group. An `int` or `float`
  carrying `[Range]` used to be a plain number field, every enum a dropdown, and each of the other types a
  read-only note.

### Fixed

- A fiber registered with `VelvetDevToolsRegistry` leaves the registry when it is disposed. An interior
  fiber registered by hand used to stay for the session as a greyed-out row until it was unregistered by
  hand. A disposed fiber is not registered, and registering a registered fiber again relabels its entry and
  keeps its registration time rather than replacing the entry.
