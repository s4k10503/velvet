### Changed

- A `contain` focus scope holds focus against every panel, as React Aria's contained `FocusScope` holds it
  against the whole document. Focus that moves to a `V.Portal(layer:)` or `V.WorldSpace` host panel, or to
  another mounted tree's panel, is pulled back into the scope on its panel's next scheduler tick, unless it
  lands in a portal declared inside the scope or inside a contained scope created after this one; before, focus
  in any other panel Velvet manages was left where it landed.

- Of two contained scopes, the one created later now keeps focus against the older one, in one panel as across
  panels and mounted trees. Before, a landing inside any contained scope stood, so focus could move from a
  newer modal back into an older one beneath it, and each of two mounted trees' contained scopes pulled focus
  back from the other's panel.

- Content of a portal declared inside a `contain` scope counts as inside it when the portal renders into
  the scope's own panel, as it now does when the portal renders into another panel. Before, focus that moved
  there was snapped back.

- Arrow and d-pad navigation stays inside a `singleTabStop` group, as it stays inside a WAI-ARIA composite
  widget. A spatial move that lands outside the group returns to the member it started from; before, the
  engine's geometric navigation could leave the group at its edge.
