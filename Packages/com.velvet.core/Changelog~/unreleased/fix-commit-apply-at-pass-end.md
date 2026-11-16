### Fixed

- A pass applied every write recorded before an element patched at once, a Motion or a relocated or wrapped element,
  when it reached that element's post-children passes, ahead of the end of the pass. Those passes now run where the
  pass applies its writes, behind the writes of the element's descendants.
