### Fixed

- A `UseStore` reader rendered in the same batch drain pass as a store mutation it was already queued for no
  longer stays on the snapshot that pass pinned. Its render read the pinned value, consistent with the readers
  rendered before the mutation, and nothing asked for another render once the mutation had notified; when the
  mutation changed what its selector returns, it now renders again in a later pass and shows the store's
  current value.
