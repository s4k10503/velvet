### Changed

- `V.TextField`'s `maxLength:` limits what the user enters, as HTML `maxlength` does. A value Velvet writes
  (the controlled `value:`, a changed `maxLength:`) and an assignment to the field's `value` from a
  `refCallback:` are shown whole where they used to be cut to the limit, and a delayed field no longer
  commits a cut copy of them on Enter or blur. The exception is a value with a line break assigned to a
  single-line field, which is still limited like an edit. A render changing `maxLength:` leaves the value
  and a pending edit as they are. Typed and pasted text is inserted only as far as it fits at the caret,
  where the field used to keep the new characters and cut from the end; text from the touch keyboard or an
  IME is still cut from the end, and neither has been tested on a device. The limit is no longer written
  to the element's own `maxLength`, so code reading that property no longer sees it. A raw
  `ChangeEvent<string>` or `InputEvent` listener registered outside `onValueChanged:` now sees the
  over-long or refused text, which the engine used to cut before sending the event; `onValueChanged:`
  still receives the value the field ended the edit with.
