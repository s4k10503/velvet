### Fixed

- Appending a row to a list whose rows each carry a ring (`ring-*`) put the new row between the last row and that
  row's ring band. With two ringed rows and a third appended, the children read `r0, band, r1, r2, band, band`; where
  the append leaves the last row's rectangle unchanged, as with fixed-size rows in a column, they still read so after a
  layout pass. The band of `r1` now stays directly after `r1`. A keyed move of a row to the end of such a list left the
  band of the row before it behind the moved row in the same way, until a layout pass placed it again.
