### Changed

- `Hooks.UseNavigation().Location` is null while the navigation state is `Idle`, as React Router's
  `navigation.location` is `undefined` then. It used to carry the committed location, the value
  `Hooks.UseLocation()` returns; read that hook for it instead.

- A loader error that no route in the matched chain declares an `errorElement` for renders a default
  error element in place of the root route, as React Router's does: a heading, the exception's message
  and its stack trace, with the exception logged in the editor and in a development build. Nothing
  rendered there before.
