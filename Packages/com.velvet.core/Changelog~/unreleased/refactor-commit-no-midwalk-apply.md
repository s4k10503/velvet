### Changed

- A pass no longer applies its recorded writes before it ends. An element patched at once in the walk (a Motion, a
  relocated or wrapped element) ran its post-children passes by applying every write recorded before it; those passes
  now run where the pass applies its writes, behind the writes of its descendants. A delayed text field is patched
  where the pass applies its writes as well, so the change handler its commit of a pending edit runs reads what the
  elements before it in the tree have been given in the same render, and the field reads its old name until then.
  A cleanup of a removed component that reads an element its parent component renders ahead of it reads the element as
  it was before this render's update of it, where it read the element updated if the walk had patched an element at
  once between them.
- A pass that starts while a pass of another context is recording no longer applies that pass's writes first. A
  callback it runs reads a live child list without the rows the other pass has inserted, and an insert, a removal or a
  move it makes into a container the other pass holds is applied where the other pass ends.
- A synchronous flush, such as a discrete event's handler asks for, and a flush of passive effects are held back while
  the pass of the context that owns them applies its recorded writes, as they are while the pass walks, so a handler an
  apply fires does not render inside the apply. The update it queues is rendered by the drain or the next frame.
