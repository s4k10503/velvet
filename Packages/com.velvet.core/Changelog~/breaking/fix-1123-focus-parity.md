### Changed

- A `contain` focus scope holds focus against every panel, as React Aria's contained `FocusScope` holds it
  against the whole document. Focus that moves to a `V.Portal(layer:)` or `V.WorldSpace` host panel is pulled
  back into the scope on its panel's next scheduler tick, unless it lands in a portal declared inside the scope
  or inside another contained scope; before, focus in any other panel Velvet manages was left where it landed.

- Arrow and d-pad navigation stays inside a `singleTabStop` group, as it stays inside a WAI-ARIA composite
  widget. A spatial move that lands outside the group returns to the member it started from; before, the
  engine's geometric navigation could leave the group at its edge.
