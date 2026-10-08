### Changed

- A `get` submission through `Router.SubmitAsync` or the function `Hooks.UseSubmit` returns encodes a string
  or name/value pairs as its query string, as React Router does, and no longer throws `ArgumentException` for
  form data that is not an `ISearchParams`: a body it cannot encode commits "Unable to encode submission
  body" as the leaf route's error and runs no action. A caller that caught the exception reads
  `Router.CurrentLoaderErrors` instead.
