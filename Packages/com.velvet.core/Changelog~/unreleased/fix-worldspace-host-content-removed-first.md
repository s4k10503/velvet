### Fixed

- Outside play mode, an update or `MountedTree.Dispose` that removed a `V.WorldSpace` destroyed its panel before it
  removed the elements inside it. A `DetachFromPanelEvent` callback of one of those elements ran as the panel was
  destroyed rather than as the element's own removal, whose error an error boundary or `MountOptions.OnUncaughtError`
  receives, and one that threw ran again when that removal came. The panel now goes once those elements have been
  removed, so each such callback runs once, as its element's removal, and its error is reported once, as React
  captures what each removal of a deletion throws.
