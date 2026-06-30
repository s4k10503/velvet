// Polyfill for [ModuleInitializer] support on .NET Standard 2.1 (Unity 6 / Mono).
// Defined here so consuming asmdefs can use [ModuleInitializer] without per-compilation generator emission,
// which avoided CS0102 collisions when other generators in the same compilation also emit the polyfill.
//
// IMPORTANT: This file relies on Velvet.asmdef having autoReferenced=true. If autoReferenced is disabled,
// user asmdefs cannot see this type and any [ModuleInitializer] annotation in generated code will fail to bind.
#if !NET5_0_OR_GREATER
namespace System.Runtime.CompilerServices
{
    [global::System.AttributeUsage(global::System.AttributeTargets.Method, Inherited = false)]
    public sealed class ModuleInitializerAttribute : global::System.Attribute { }
}
#endif
