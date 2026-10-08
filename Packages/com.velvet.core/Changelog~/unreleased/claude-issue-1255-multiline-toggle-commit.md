### Fixed

- Turning `V.TextField`'s `multiline:` off and back on over a delayed field no longer leaves text nobody
  typed on screen for a blur to commit. The field shows the value again, with its line breaks and up to
  the current `maxLength:`. Text the user typed before the toggle stays, minus its line breaks while
  `multiline:` is off.
