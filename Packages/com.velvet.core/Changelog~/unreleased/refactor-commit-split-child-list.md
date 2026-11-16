### Changed

- A callback that runs while a pass is still creating rows, such as the `onCreated` of `V.ScrollView`, reads the
  child list of a container the pass did not create without the rows the pass has inserted so far. The pass applies
  them at the end of its time slice. For example, in a container that held one row, a row created after two inserted rows read a `childCount` of 3 before and reads 1 now;
  and with a pass that parks after its first row, the rows created in the resumed slice read the count the parked slice
  left, where each used to read one more than the row before it.
