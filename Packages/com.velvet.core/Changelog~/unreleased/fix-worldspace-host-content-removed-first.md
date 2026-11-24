### Fixed

- Outside play mode, an update or `MountedTree.Dispose` that removed a `V.WorldSpace` destroyed its panel before it
  removed the elements directly inside the `V.WorldSpace`, so their `DetachFromPanelEvent` callbacks ran as the panel
  was destroyed. Where one of them threw there, its error went to the log instead of to an error boundary or
  `MountOptions.OnUncaughtError`, and that callback ran a second time at its element's own removal. The panel now goes
  once those elements have been removed, so each of their callbacks runs once, at its element's removal, and what it
  throws goes where the error of any other removal goes, as React captures what each removal of a deletion throws.
