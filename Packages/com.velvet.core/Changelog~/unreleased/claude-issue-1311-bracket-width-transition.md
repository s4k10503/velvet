### Fixed

- Swapping one bracketed length for another of the same property on an element with a `transition-*`
  utility (`w-[300px]` to `w-[10px]`, or to `!w-[10px]`) now animates from the old value, as a CSS
  width change does, instead of landing at once. The outgoing class no longer clears the inline value
  before the incoming class writes over it.
