### Fixed

- A recycled element kept any inline style property the pool reset did not name — a cursor or a text shadow written to
  it, for one — and showed it on its next use. The reset now clears every inline style property.
