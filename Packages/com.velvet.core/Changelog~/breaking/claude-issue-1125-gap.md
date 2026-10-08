### Changed

- A `get` submission through `Router.SubmitAsync` or the function `Hooks.UseSubmit` returns reads its form
  data as React Router's `new URLSearchParams(body)` does: a string, a dictionary, a sequence of pairs or an
  object's declared members become the query string, strings, dictionaries and pair sequences in the order
  written. A body that cannot be encoded, including one that throws while it is read, commits "Unable to
  encode submission body" as the leaf route's error and runs no action. Only a `get` encodes its body. It no
  longer throws `ArgumentException` for form data that is not an `ISearchParams`; a caller that caught the
  exception reads `Router.CurrentLoaderErrors` instead.
