### Changed

- A component re-rendering on its own state no longer spends time that grows with the number of siblings in its
  parent's output when that output holds no `V.Provider` or `Motion`. In a list of 2,000 memoized rows, a row
  updating its own state took about 0.58 ms against 0.13 ms at 250 rows, and now takes about 0.05 ms at both.
