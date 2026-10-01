### Fixed

- An error boundary that catches an element callback's error, such as one an `onCreated` throws, while a
  component below it renders an update shows its fallback, as React does. The rest of that update's render
  went on committing after the catch: a row it matched by position was rewritten over the row the fallback
  had moved into that position, and a `V.Suspense` whose content was still waiting put its fallback over the
  boundary's.
