### Changed

- A pass no longer applies its recorded writes before it ends. An element patched at once in the walk (a Motion, a
  relocated or wrapped element) ran its post-children passes by applying every write recorded before it; those passes
  now run where the pass applies its writes, behind the writes of its descendants.
- A delayed text field that stays in its own slot, with no `clip-path` class and none of `absolute`, `relative` or
  `z-*`, is patched where the pass applies its writes. The change handler its commit of a pending edit runs reads what
  the rows before the field in its parent have been given by the same render.
- A pass that starts inside a pass of another context no longer applies that pass's writes first. A callback it runs
  reads a live child list without the rows the other pass has inserted, an insert, a removal or a move it makes into a
  container the other pass holds is applied where the other pass ends, and its z-layer settles and arrangement are made
  where the outermost pass ends.
- No context renders or flushes passive effects while a pass of any context records or applies its writes, so a handler
  an apply fires does not render inside it. A context other than the one holding the pass renders what the handler
  queued where the outermost pass ends.
