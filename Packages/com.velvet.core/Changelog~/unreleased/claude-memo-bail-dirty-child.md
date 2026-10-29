### Fixed

- A component that is not memoized, below a memoized row whose props did not change, was rendered again by its
  ancestor's re-render whenever another component below the same row had an update pending in that pass, and
  not otherwise. It is now left as it was in both cases, and only the component with the update pending
  renders, as with `React.memo`.
