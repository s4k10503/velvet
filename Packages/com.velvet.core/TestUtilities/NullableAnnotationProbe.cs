#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reflection;

namespace Velvet.TestUtilities
{
    /// <summary>
    /// Reads back the nullable reference annotations the compiler emitted for a signature.
    /// </summary>
    /// <remarks>
    /// The compiler catches a disagreement between a member and the interface it implements (CS8766); it
    /// reported nothing for <see cref="Hooks.UseLocation"/>, whose null arrived through a null-forgiving
    /// operator one call away.
    /// <para>
    /// Two guards read this, and they are not the same guard, which is worth saying because a reader
    /// meeting both will otherwise see duplication and remove one. <c>PublicAPI.txt</c> covers every
    /// shipped member without anyone writing a case, and catches DRIFT — the file changed, so something
    /// moved. It cannot catch a regression: the file is maintained by regenerating it, so an annotation
    /// going back to <c>T</c> is recorded as the new expected value the next time somebody does. A probe
    /// test asserts what one member's annotation OUGHT to be and carries the reason, so it fails on that
    /// regression rather than absorbing it. The broad one has no opinion; the narrow one does.
    /// </para>
    /// </remarks>
    public static class NullableAnnotationProbe
    {
        /// <summary>Nullable reference states, valued as the compiler encodes them.</summary>
        public enum Annotation
        {
            /// <summary>Declared outside a nullable context.</summary>
            Oblivious = 0,

            /// <summary>Declared non-nullable (<c>T</c>).</summary>
            NotNullable = 1,

            /// <summary>Declared nullable (<c>T?</c>).</summary>
            Nullable = 2,
        }

        private const string NullableAttributeName = "System.Runtime.CompilerServices.NullableAttribute";

        private const string NullableContextAttributeName =
            "System.Runtime.CompilerServices.NullableContextAttribute";

        /// <summary>
        /// Returns the declared annotation of <paramref name="method"/>'s return type.
        /// </summary>
        /// <param name="method">Method whose return type is read. Must not be null.</param>
        public static Annotation ReturnAnnotation(MethodInfo method)
        {
            if (method == null) throw new ArgumentNullException(nameof(method));

            return Read(method.ReturnParameter.GetCustomAttributesData(), method).Next(method.ReturnType);
        }

        /// <summary>
        /// Opens a reader over one signature position — a return type, a parameter, a field, a property or an
        /// event handler type — whose annotations are then drawn off in the order
        /// <see cref="AnnotationReader.Next"/> documents.
        /// </summary>
        /// <param name="site">Attributes declared on that position itself. Must not be null.</param>
        /// <param name="scope">
        /// Member the position belongs to, for a property or an event its accessor: a member agreeing with the
        /// enclosing <c>NullableContext</c> carries no attribute of its own, so an absent attribute is a
        /// reading to be resolved outward rather than a missing one.
        /// </param>
        public static AnnotationReader Read(IList<CustomAttributeData> site, MemberInfo scope)
        {
            if (site == null) throw new ArgumentNullException(nameof(site));
            if (scope == null) throw new ArgumentNullException(nameof(scope));

            var declared = ReadArgument(site, NullableAttributeName);
            if (declared.Flags != null) return new AnnotationReader(declared.Flags, 0);

            return declared.Uniform.HasValue
                ? new AnnotationReader(null, declared.Uniform.Value)
                : new AnnotationReader(null, ContextFor(scope));
        }

        private static byte ContextFor(MemberInfo scope)
        {
            if (scope is MethodBase method)
            {
                var onMethod = ReadArgument(method.GetCustomAttributesData(), NullableContextAttributeName);
                if (onMethod.Uniform.HasValue) return onMethod.Uniform.Value;
            }

            for (var type = scope as Type ?? scope.DeclaringType; type != null; type = type.DeclaringType)
            {
                var onType = ReadArgument(type.GetCustomAttributesData(), NullableContextAttributeName);
                if (onType.Uniform.HasValue) return onType.Uniform.Value;
            }

            var onModule = ReadArgument(scope.Module.GetCustomAttributesData(), NullableContextAttributeName);
            return onModule.Uniform ?? (byte)Annotation.Oblivious;
        }

        private static (byte? Uniform, IReadOnlyList<byte>? Flags) ReadArgument(
            IList<CustomAttributeData> attributes,
            string attributeFullName)
        {
            foreach (var attribute in attributes)
            {
                if (attribute.AttributeType.FullName != attributeFullName) continue;
                if (attribute.ConstructorArguments.Count != 1) continue;

                var argument = attribute.ConstructorArguments[0];
                if (argument.Value is byte uniform) return (uniform, null);

                if (argument.Value is ReadOnlyCollection<CustomAttributeTypedArgument> flags)
                {
                    var bytes = new byte[flags.Count];
                    for (var index = 0; index < flags.Count; index++)
                    {
                        bytes[index] = flags[index].Value is byte flag ? flag : (byte)Annotation.Oblivious;
                    }

                    return (null, bytes);
                }
            }

            return (null, null);
        }

        /// <summary>
        /// Draws the annotations of one signature position off in order, opened by
        /// <see cref="NullableAnnotationProbe.Read"/>.
        /// </summary>
        public sealed class AnnotationReader
        {
            private readonly IReadOnlyList<byte>? flags;
            private readonly byte uniform;
            private int index;
            private int overrun;

            internal AnnotationReader(IReadOnlyList<byte>? flags, byte uniform)
            {
                this.flags = flags;
                this.uniform = uniform;
            }

            /// <summary>
            /// Whether the compiler spelled this position out flag by flag, which is the only form in which
            /// <see cref="Misalignment"/> can report anything: one flag standing for the whole signature
            /// cannot be read off by one.
            /// </summary>
            public bool ReadsFlagArray => flags != null;

            /// <summary>
            /// Flags the compiler emitted that <see cref="Next"/> was never asked for, and the count by which
            /// it was asked past the end. Both are zero exactly when <see cref="Next"/> was driven over the
            /// same positions the compiler wrote, which is what
            /// <c>Given_ShippedAssemblies_When_EverySignaturePositionIsRead_Then_NoFlagIsMisread</c> asserts
            /// across the shipped surface.
            /// </summary>
            public (int Unread, int Overrun) Misalignment =>
                (flags == null ? 0 : flags.Count - index, overrun);

            /// <summary>
            /// Returns the annotation of the next position and advances past it. Call this once per node of a
            /// type tree, in pre-order — a constructed type before its arguments, an array before its element
            /// — since that is the order the compiler flattens the flags into.
            /// </summary>
            /// <param name="type">Type at that node. Must not be null.</param>
            public Annotation Next(Type type)
            {
                if (type == null) throw new ArgumentNullException(nameof(type));
                if (!OccupiesPosition(type)) return Annotation.Oblivious;
                if (flags == null) return (Annotation)uniform;

                if (index >= flags.Count)
                {
                    overrun++;
                    return Annotation.Oblivious;
                }

                return (Annotation)flags[index++];
            }
        }

        /// <summary>
        /// Whether the compiler wrote a flag for this node. Which shapes take one is Roslyn's to decide, so
        /// <c>NullableAnnotationRenderingTests</c> carries a case per rule below and fails when one moves.
        /// </summary>
        private static bool OccupiesPosition(Type type)
        {
            if (type.IsByRef || type.IsPointer) return false;
            if (type.IsGenericParameter || type.IsArray) return true;
            if (IsNullableValueType(type)) return false;
            return type.IsGenericType || !type.IsValueType;
        }

        private static bool IsNullableValueType(Type type) =>
            type.IsGenericType
            && !type.IsGenericTypeDefinition
            && type.GetGenericTypeDefinition() == typeof(Nullable<>);
    }
}
