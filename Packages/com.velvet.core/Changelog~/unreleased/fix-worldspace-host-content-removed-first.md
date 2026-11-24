### Fixed

- Outside play mode, an update or `MountedTree.Dispose` that removed a `V.WorldSpace` destroyed its panel before it
  removed the elements the `V.WorldSpace` held, so their `DetachFromPanelEvent` callbacks ran as the panel was
  destroyed rather than as each element's own removal, whose error an error boundary or
  `MountOptions.OnUncaughtError` receives; the first that threw also kept the callbacks of the elements after it from
  running there. The panel now goes once those elements have been removed, so each of their callbacks runs at its
  element's removal, and what it throws goes where the error of any other removal goes, as React captures what each
  removal of a deletion throws.
