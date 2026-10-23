### Fixed

- A resource that faults below an error boundary inside a `V.Suspense` has the Suspense render again, so the error
  boundary catches the error in that render and the Suspense reveals the error boundary's fallback, as React's
  retry does. The Suspense stayed on its fallback, and the error boundary's fallback was reconciled outside it.
- A component in a `V.Suspense`'s fallback whose own update suspends goes to the Suspense above that one, as
  React renders a fallback under the Suspense above it, and so does a resource of it that resolves. The component
  rendering the inner Suspense was asked to render first, and rendered its children again.
- A component whose first render throws in the update of a component below the error boundary that catches it
  is disposed, as React discards it: what its hooks took before the throw, a `Hooks.UseStore` subscription among
  them, is released. It stayed registered and queued for a layout commit.
