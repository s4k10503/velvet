### Fixed

- A render that suspends with no `V.Suspense` above it no longer commits the effects of the components that
  rendered ahead of the suspend. Their effects, including those of components the render mounted, and their
  imperative handles used to run for the render that was given up and then be skipped when the retry
  committed; they now run with the retry. An effect still pending from an earlier commit runs that commit's
  closure rather than the one a discarded render registered. A component that already existed keeps the props
  it last committed, so a render of it alone before the resource resolves uses them. The elements the walk
  had patched ahead of the suspend stay patched, and a suspend in a later slice of a time-sliced render still
  finds the components of the earlier slices committed.
