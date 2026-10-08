### Changed

- A `get` submission through `Router.SubmitAsync` or the function `Hooks.UseSubmit` returns encodes its form
  data as React Router's `new URLSearchParams(body)` does, in the order the body holds its pairs: a string, a
  dictionary, a sequence of pairs or an object's public properties become the query string, and a body that
  cannot be encoded commits "Unable to encode submission body" as the leaf route's error and runs no action.
  It no longer throws `ArgumentException` for form data that is not an `ISearchParams`; a caller that caught
  the exception reads `Router.CurrentLoaderErrors` instead.
