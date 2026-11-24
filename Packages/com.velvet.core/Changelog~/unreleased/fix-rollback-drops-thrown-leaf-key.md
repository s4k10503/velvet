### Fixed

- An error boundary that caught an error thrown while an element of its output was being created or updated could log
  "Duplicate key detected among new siblings" as its fallback rendered, though no two siblings shared a key. It no
  longer does.
