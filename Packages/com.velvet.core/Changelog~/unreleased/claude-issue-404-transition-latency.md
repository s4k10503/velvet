### Fixed

- A `UseStore` reader rendered in the same batch drain pass as a store mutation, after it, no longer stays on
  the snapshot that pass pinned — whether it was already queued when the mutation notified it or was mounted
  later in that pass, after the notification it never received. Its render read the pinned value, consistent
  with the readers rendered before the mutation, and nothing asked for another render; when the mutation
  changed what its selector returns, it now renders again in a later pass and shows the store's current value.
