### Changed

- A pass no longer applies its recorded writes before it ends. An element patched at once in the walk (a Motion, a
  relocated or wrapped element, or a delayed text field) ran its post-children passes by applying every write recorded
  before it; those passes now run where the pass applies its writes, behind the writes of its descendants. A cleanup of
  a removed component that reads an element updated earlier in the same tree reads it as it was before the update,
  where it read it updated if the walk had patched an element at once after the update.
- A pass that starts while another context's pass is recording no longer applies that pass's writes first, so a
  callback it runs reads a live child list without the rows the other pass has inserted.
- A synchronous flush, such as a discrete event's handler asks for, is held back while a pass applies its recorded
  writes, as it is while the pass walks, so a handler an apply fires does not render inside the apply.
