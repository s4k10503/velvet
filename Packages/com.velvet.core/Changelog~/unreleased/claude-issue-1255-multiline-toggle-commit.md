### Fixed

- Turning `V.TextField`'s `multiline:` off and back on over a delayed field whose value has a line break and
  a `maxLength:` no longer leaves the break-less text on screen as if it had been typed, which blurring
  then committed as a new value. The field shows the value up to the limit again, with its break.
