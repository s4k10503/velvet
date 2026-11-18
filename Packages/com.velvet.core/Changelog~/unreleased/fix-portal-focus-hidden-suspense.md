### Fixed

- A `V.Portal` that mounted while its `V.Suspense` kept its children hidden left the focus an `autoFocus`
  `V.FocusScope` inside it had taken on the hidden button. The focus now leaves when the portal's content is hidden,
  as it does for the children the boundary hides.
