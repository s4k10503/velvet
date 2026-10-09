### Added

- `V.TextField` takes `onKeyDown:`, `onKeyUp:`, `onFocus:`, `onBlur:`, `onSubmit:` and `onCreated:`, so a
  commit-on-Enter field, a chat box clearing itself on send or a field reacting to leaving focus no
  longer needs a `V.Motion` wrapper or a `refCallback:` registering callbacks by hand. `onKeyDown:`
  runs before the field takes the key, and stopping the event's propagation keeps the key out of it.
  `onFocus:` and `onBlur:` report focus entering and leaving the field as a whole, not the step where
  the field hands focus from its input to itself on Enter. `onSubmit:` receives the value on the Enter
  in a single-line field, after a delayed field has released its text, as the browser's implicit form
  submission does: a read-only field submits, the input keeps focus and its caret across the
  submitting Enter, the Enter
  arriving while an IME composition is open does not, Enter with Ctrl or Command held and Alt not held
  does not, and a multi-line field never submits. The soft keyboard's Done submits too, read from the
  keyboard's status when it blurs the field; that reading has not yet been verified on a device.
  `Velvet.Experimental.VTextField` carries the five handlers as `OnKeyDown`, `OnKeyUp`, `OnFocus`,
  `OnBlur` and `OnSubmit`.

### Changed

- A `KeyDownBinding` or `KeyUpBinding` on a text-input element now runs on the element's trickle-down
  pass rather than its bubble pass, so it runs before a callback the element or a child registered for
  the bubble pass, and stopping propagation there keeps the key from the input. See Fixed below for
  what the move repairs.

### Fixed

- A `KeyDownBinding` or `KeyUpBinding` handed to `V.Motion(elementType: typeof(TextField), events:)`,
  or to any other text-input element type, saw none of the keys the field's input takes, because the
  input stops their propagation before a callback on the field's bubble pass runs. Both now run on
  the field's trickle-down pass, ahead of the input, as `V.TextField`'s `onKeyDown:` and `onKeyUp:` do.
