### Changed

- A callback that runs while a pass is still creating rows, such as the `onCreated` of `V.ScrollView`, reads the
  child list of a container the pass did not create with the rows that pass has inserted so far left off; they are
  on the list when the pass ends. A row created after two inserted rows read a `childCount` of 3 before and reads 1
  now. In a pass resumed after a time-sliced park, the four rows created across the two slices read 0, 1, 2 and 3
  before and read 0, 1, 1 and 1 now: the rows a slice inserts are on the list from the end of that slice.
