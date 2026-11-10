### Fixed

- The compiler no longer caches the value a local is given where the only cached scope reading that local sits
  inside the computation of the value, which reads what the local held before. Such a value took a cache slot
  and a key of its own on every render with nothing reading what it stored.
