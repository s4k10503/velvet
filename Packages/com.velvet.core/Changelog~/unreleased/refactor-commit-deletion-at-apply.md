### Changed

- The cleanups of components a render stops rendering, and of the removed elements' refs, now run once the render has
  finished walking, ahead of the layout effects, as React's do in the commit rather than while it renders. A
  component rendered later in the same pass therefore still sees the removed one mounted, and a cleanup that throws
  reaches its error boundary in a follow-up commit without stopping the render under way. A removal that holds a
  Portal, or a component that mounts its own wrapper element, still runs its cleanups while the render walks.
