// The rules this solution implements apply to it too. The wiring that makes that possible is
// Velvet.SourceGenerators.Bootstrap, which every project here already references as an analyzer;
// this marker is what turns the rules on, and it goes in last because they are error severity — an
// assembly that opts in with a backlog cannot build.
[assembly: System.Reflection.AssemblyMetadata("Velvet.CodeShape", "enforce")]
