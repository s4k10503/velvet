using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using NUnit.Framework;
using Velvet.Editor.DevTools;
using Velvet.TestUtilities;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the shipped assemblies' public and protected member surface to a checked-in file so every
    /// addition, removal and nullability change is a reviewable diff. The CHANGELOG's
    /// [Unreleased — breaking] section is written down by hand; nothing else compares two commits.
    /// </summary>
    [TestFixture]
    internal sealed class PublicApiSurfaceTests
    {
        // Regeneration is opt-in so a normal test run never rewrites the pin file.
        private const string UpdateEnvironmentVariable = "VELVET_UPDATE_PUBLIC_API";

        private static readonly string PublicApiPath =
            Path.GetFullPath("Packages/com.velvet.core/PublicAPI.txt");

        [Test]
        public void Given_ShippedAssemblies_When_PublicApiSurfaceIsRendered_Then_ItMatchesPublicApiTxt()
        {
            // Arrange
            var rendered = PublicApiSurface.RenderShippedAssemblies().ToArray();

            // Act
            if (string.Equals(
                    Environment.GetEnvironmentVariable(UpdateEnvironmentVariable),
                    "1",
                    StringComparison.Ordinal))
            {
                File.WriteAllLines(PublicApiPath, rendered);
            }

            var onDisk = File.Exists(PublicApiPath)
                ? File.ReadAllLines(PublicApiPath)
                : Array.Empty<string>();
            var added = rendered.Except(onDisk, StringComparer.Ordinal).OrderBy(line => line, StringComparer.Ordinal)
                .ToArray();
            var removed = onDisk.Except(rendered, StringComparer.Ordinal)
                .OrderBy(line => line, StringComparer.Ordinal).ToArray();

            // Assert
            Assert.That(
                (added.Length, removed.Length),
                Is.EqualTo((0, 0)),
                BuildDriftMessage(added, removed));
        }

        [Test]
        public void Given_ShippedAssemblies_When_EverySignaturePositionIsRead_Then_NoFlagIsMisread()
        {
            // Arrange
            var diagnostics = new SurfaceDiagnostics();

            // Act
            foreach (var assembly in PublicApiSurface.ShippedAssemblies)
            {
                PublicApiSurface.Render(assembly, diagnostics);
            }

            // Assert — a walk that read no flag array at all would report nothing misread either.
            Assert.That(
                (read: diagnostics.FlagArraySignatures > 0, misread: string.Join("\n", diagnostics.Misread)),
                Is.EqualTo((read: true, misread: string.Empty)),
                "A signature position the walk misses shifts every annotation after it onto the wrong type, "
                + "so the surface file would pin a spelling no source declares.");
        }

        [Test]
        public void Given_CodeGenAssembly_When_ItsSurfaceTypesAreCollected_Then_ItHasNone()
        {
            // Arrange
            var codeGen = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(
                assembly => string.Equals(
                    assembly.GetName().Name,
                    "Unity.Velvet.CodeGen",
                    StringComparison.Ordinal));

            // Act
            var surface = codeGen?.GetTypes().Where(PublicApiSurface.IsSurfaceType).Select(type => type.FullName)
                          ?? Enumerable.Empty<string>();

            // Assert
            Assert.That(
                (loaded: codeGen != null, surface: string.Join(", ", surface)),
                Is.EqualTo((loaded: true, surface: string.Empty)),
                "The ILPP assembly is left out of PublicAPI.txt because a consumer can bind to nothing in "
                + "it. An unloaded assembly would report the same empty surface, so both are read at once.");
        }

        /// <summary>A generic with a nested type, which the repository's own surface gains with
        /// `VelvetTask` and did not carry when this rendering was written.</summary>
        private sealed class OwnerWithNested<T>
        {
            internal struct Nested
            {
                internal T Held;
            }
        }

        // GREEN_ON_BASE(characterization): the rendering under test sits in this same file, which
        // the base lane carries with the cases, so no base run can separate them. Restoring the
        // first-backtick cut by hand and re-running the fixture fails both, which is the reading.
        [Test]
        public void Given_ATypeNestedInsideAGeneric_When_Rendered_Then_ItIsNotItsOwner()
        {
            // Arrange — the two rendered the same line, so the nested type's members were recorded
            // against its owner and an addition to either produced the same diff.
            var owner = PublicApiSurface.FormatType(typeof(OwnerWithNested<>));
            var nested = PublicApiSurface.FormatType(
                typeof(OwnerWithNested<>.Nested));

            // Act / Assert — the owner naming itself rides along, because two renderings that both
            // collapsed to something else would differ from each other and be no more use.
            Assert.That((owner.Contains("OwnerWithNested"), nested.Contains("Nested"), owner != nested),
                        Is.EqualTo((true, true, true)));
        }

        // GREEN_ON_BASE(characterization): the rendering under test sits in this same file, which
        // the base lane carries with the cases, so no base run can separate them. Restoring the
        // first-backtick cut by hand and re-running the fixture fails both, which is the reading.
        [Test]
        public void Given_ATypeNestedInsideAGeneric_When_Rendered_Then_ItsOwnerIsNamedInIt()
        {
            // Arrange — the non-generic case already rendered the owner in front of the `+`, and this
            // is the same spelling one arity marker along. What the marker looks like is read off the
            // owner rather than written down, so this says nothing about how the runtime spells one.
            var owner = PublicApiSurface.FormatType(typeof(OwnerWithNested<>));
            var nested = PublicApiSurface.FormatType(
                typeof(OwnerWithNested<>.Nested));

            // Act / Assert
            Assert.That(nested.StartsWith(owner.Substring(0, owner.IndexOf('`')), StringComparison.Ordinal)
                        && nested.Contains("+Nested"),
                        Is.True, $"owner={owner} nested={nested}");
        }

        private static string BuildDriftMessage(IReadOnlyList<string> added, IReadOnlyList<string> removed)
        {
            var message = "Public API surface drifted from Packages/com.velvet.core/PublicAPI.txt.";
            if (added.Count > 0)
            {
                message += "\n\nAdded:\n" + string.Join("\n", added);
            }

            if (removed.Count > 0)
            {
                message += "\n\nRemoved:\n" + string.Join("\n", removed);
            }

            message += "\n\nTo regenerate PublicAPI.txt, run:\n"
                       + "VELVET_UPDATE_PUBLIC_API=1 \"$UNITY\" -runTests -batchmode -projectPath \"$PWD\" "
                       + "-testPlatform EditMode -testFilter Velvet.Tests.PublicApiSurfaceTests";
            return message;
        }
    }

    /// <summary>
    /// What one render made of the compiler's nullable flags, for a fixture asserting it read them the way
    /// they were written.
    /// </summary>
    internal sealed class SurfaceDiagnostics
    {
        /// <summary>Signatures whose annotation arrived as a flag array, the only form a walk can misread.</summary>
        public int FlagArraySignatures { get; set; }

        /// <summary>Signatures whose flags and type tree did not line up, one description each.</summary>
        public List<string> Misread { get; } = new();
    }

    internal static class PublicApiSurface
    {
        private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        /// <summary>
        /// The package's shipped assemblies a consumer can bind to. <c>Unity.Velvet.CodeGen</c> is the third
        /// one it ships and declares no public type, which
        /// <c>Given_CodeGenAssembly_When_ItsSurfaceTypesAreCollected_Then_ItHasNone</c> holds.
        /// </summary>
        public static IReadOnlyList<Assembly> ShippedAssemblies { get; } = new[]
        {
            typeof(V).Assembly,
            typeof(VelvetDevToolsWindow).Assembly,
        };

        public static IReadOnlyList<string> RenderShippedAssemblies()
        {
            var lines = new List<string>();
            foreach (var assembly in ShippedAssemblies)
            {
                lines.AddRange(Render(assembly, new SurfaceDiagnostics()));
            }

            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        /// <param name="diagnostics">Reads back what the walk over the nullable flags did.</param>
        public static IReadOnlyList<string> Render(Assembly assembly, SurfaceDiagnostics diagnostics)
        {
            var prefix = "[" + assembly.GetName().Name + "] ";
            var lines = new List<string>();
            foreach (var type in assembly.GetTypes()
                         .Where(IsSurfaceType)
                         .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                lines.Add(prefix + RenderType(type));
                lines.AddRange(RenderMembers(type, diagnostics).Select(line => prefix + line));
            }

            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        /// <summary>Whether a consumer outside the assembly can name this type.</summary>
        /// <remarks>
        /// A nested type's own accessibility is not the whole answer: <c>public</c> nested in
        /// <c>internal</c> is unnameable from outside, and three such records were being rendered here.
        /// Nineteen lines describing an API nothing can bind to is not wrong so much as it is noise a real
        /// change hides in — a diff against them reads as a public-surface change and is not one.
        /// </remarks>
        public static bool IsSurfaceType(Type type)
        {
            for (var link = type; link != null; link = link.DeclaringType)
            {
                if (link.IsNested ? !link.IsNestedPublic : !link.IsPublic)
                {
                    return false;
                }
            }

            return !IsCompilerGenerated(type);
        }

        /// <summary>
        /// Renders one type's members, for a fixture that pins how a signature is spelled rather than what
        /// the package ships.
        /// </summary>
        public static IReadOnlyList<string> RenderMembersOf(Type type)
        {
            var lines = RenderMembers(type, new SurfaceDiagnostics()).ToList();
            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        private static IEnumerable<string> RenderMembers(Type type, SurfaceDiagnostics diagnostics)
        {
            foreach (var constructor in type.GetConstructors(MemberFlags))
            {
                var line = RenderConstructor(type, constructor, diagnostics);
                if (line != null)
                {
                    yield return line;
                }
            }

            foreach (var method in type.GetMethods(MemberFlags))
            {
                var line = RenderMethod(type, method, diagnostics);
                if (line != null)
                {
                    yield return line;
                }
            }

            foreach (var property in type.GetProperties(MemberFlags))
            {
                var line = RenderProperty(type, property, diagnostics);
                if (line != null)
                {
                    yield return line;
                }
            }

            foreach (var field in type.GetFields(MemberFlags))
            {
                var line = RenderField(type, field, diagnostics);
                if (line != null)
                {
                    yield return line;
                }
            }

            foreach (var eventInfo in type.GetEvents(MemberFlags))
            {
                var line = RenderEvent(type, eventInfo, diagnostics);
                if (line != null)
                {
                    yield return line;
                }
            }
        }

        private static string RenderType(Type type) => "type " + FormatType(type);

        private static string RenderConstructor(
            Type declaringType,
            ConstructorInfo constructor,
            SurfaceDiagnostics diagnostics)
        {
            if (!IsVisibleMethod(constructor) || IsCompilerGenerated(constructor))
            {
                return null;
            }

            return Prefix("ctor", constructor)
                   + FormatType(declaringType)
                   + ".ctor("
                   + FormatParameters(constructor, diagnostics)
                   + "): System.Void";
        }

        private static string RenderMethod(
            Type declaringType,
            MethodInfo method,
            SurfaceDiagnostics diagnostics)
        {
            if (method.IsSpecialName || !IsVisibleMethod(method) || IsCompilerGenerated(method))
            {
                return null;
            }

            var name = method.Name;
            if (method.IsGenericMethodDefinition || method.IsGenericMethod)
            {
                name += "`" + method.GetGenericArguments().Length;
            }

            return Prefix("method", method)
                   + FormatType(declaringType)
                   + "."
                   + name
                   + "("
                   + FormatParameters(method, diagnostics)
                   + "): "
                   + FormatAnnotated(
                       method.ReturnType,
                       method.ReturnParameter.GetCustomAttributesData(),
                       method,
                       diagnostics);
        }

        private static string RenderProperty(
            Type declaringType,
            PropertyInfo property,
            SurfaceDiagnostics diagnostics)
        {
            if (IsCompilerGenerated(property) || !HasVisibleAccessor(property, out var accessor))
            {
                return null;
            }

            var indexer = property.GetIndexParameters();
            var indexerSuffix = indexer.Length == 0
                ? string.Empty
                : "[" + FormatParameters(indexer, accessor, diagnostics) + "]";

            return Prefix("property", accessor)
                   + FormatType(declaringType)
                   + "."
                   + property.Name
                   + indexerSuffix
                   + ": "
                   + FormatAnnotated(
                       property.PropertyType,
                       property.GetCustomAttributesData(),
                       accessor,
                       diagnostics);
        }

        private static string RenderField(
            Type declaringType,
            FieldInfo field,
            SurfaceDiagnostics diagnostics)
        {
            if (field.IsSpecialName || !IsVisibleField(field) || IsCompilerGenerated(field))
            {
                return null;
            }

            return Prefix("field", field)
                   + FormatType(declaringType)
                   + "."
                   + field.Name
                   + ": "
                   + FormatAnnotated(
                       field.FieldType,
                       field.GetCustomAttributesData(),
                       field,
                       diagnostics);
        }

        private static string RenderEvent(
            Type declaringType,
            EventInfo eventInfo,
            SurfaceDiagnostics diagnostics)
        {
            if (IsCompilerGenerated(eventInfo) || !HasVisibleAccessor(eventInfo, out var accessor))
            {
                return null;
            }

            return Prefix("event", accessor)
                   + FormatType(declaringType)
                   + "."
                   + eventInfo.Name
                   + ": "
                   + FormatAnnotated(
                       eventInfo.EventHandlerType,
                       eventInfo.GetCustomAttributesData(),
                       accessor,
                       diagnostics);
        }

        private static bool HasVisibleAccessor(PropertyInfo property, out MethodInfo accessor)
        {
            accessor = ChooseVisibleAccessor(property.GetMethod, property.SetMethod);
            return accessor != null;
        }

        private static bool HasVisibleAccessor(EventInfo eventInfo, out MethodInfo accessor)
        {
            accessor = ChooseVisibleAccessor(eventInfo.AddMethod, eventInfo.RemoveMethod);
            return accessor != null;
        }

        private static MethodInfo ChooseVisibleAccessor(MethodInfo first, MethodInfo second)
        {
            if (first != null && IsVisibleMethod(first))
            {
                return first;
            }

            if (second != null && IsVisibleMethod(second))
            {
                return second;
            }

            return null;
        }

        private static bool IsVisibleMethod(MethodBase method) =>
            method != null && (method.IsPublic || method.IsFamily);

        private static bool IsVisibleField(FieldInfo field) =>
            field.IsPublic || field.IsFamily;

        private static bool IsCompilerGenerated(MemberInfo member) =>
            member.GetCustomAttribute<CompilerGeneratedAttribute>() != null;

        private static string Prefix(string kind, MemberInfo member)
        {
            var visibility = member switch
            {
                MethodBase { IsFamily: true } => "protected ",
                FieldInfo { IsFamily: true } => "protected ",
                _ => string.Empty
            };

            return kind + " " + visibility;
        }

        private static string FormatParameters(MethodBase method, SurfaceDiagnostics diagnostics) =>
            FormatParameters(method.GetParameters(), method, diagnostics);

        private static string FormatParameters(
            IReadOnlyList<ParameterInfo> parameters,
            MethodBase scope,
            SurfaceDiagnostics diagnostics) =>
            string.Join(", ", parameters.Select(parameter => FormatAnnotated(
                parameter.ParameterType,
                parameter.GetCustomAttributesData(),
                scope,
                diagnostics)));

        private static string FormatAnnotated(
            Type type,
            IList<CustomAttributeData> site,
            MemberInfo scope,
            SurfaceDiagnostics diagnostics)
        {
            var reader = NullableAnnotationProbe.Read(site, scope);
            var formatted = FormatType(type, reader);
            if (reader.ReadsFlagArray)
            {
                diagnostics.FlagArraySignatures++;
            }

            var misalignment = reader.Misalignment;
            if (misalignment != (0, 0))
            {
                diagnostics.Misread.Add(
                    $"{scope.DeclaringType?.FullName}.{scope.Name}: {formatted} "
                    + $"(unread {misalignment.Unread}, overrun {misalignment.Overrun})");
            }

            return formatted;
        }

        // `Owner`1+Nested`2` carries one per segment, and the arity below is read off the type rather
        // than off the name.
        private static readonly Regex ArityMarker = new(@"`\d+", RegexOptions.Compiled);

        internal static string FormatType(Type type) => FormatType(type, null);

        private static string FormatType(Type type, NullableAnnotationProbe.AnnotationReader reader)
        {
            if (type.IsByRef)
            {
                return FormatType(type.GetElementType()!, reader) + "&";
            }

            if (type.IsArray)
            {
                // Read before the element: inlining this call into the expression below would hand the
                // element's flag to the array.
                var annotation = Annotate(type, reader);
                var rank = type.GetArrayRank();
                var suffix = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
                return FormatType(type.GetElementType()!, reader) + suffix + annotation;
            }

            if (type.IsGenericParameter)
            {
                return type.Name + Annotate(type, reader);
            }

            if (type.IsGenericType)
            {
                var annotation = Annotate(type, reader);
                var definition = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
                // Every arity marker rather than the text before the first one. A type nested inside a
                // generic spells its FullName `Owner`1+Nested`, so cutting at the first backtick dropped
                // `+Nested` and rendered the nested type as its owner -- same line, same members recorded
                // against the wrong type, and a reviewer's diff unable to tell an addition on one from an
                // addition on the other. The non-generic owner was already correct, which is why nothing
                // reported it.
                var definitionName = ArityMarker.Replace(definition.FullName ?? definition.Name, "");
                var baseName = definitionName;
                var arity = definition.GetGenericArguments().Length;

                // A signature written in terms of the declaring type's own parameters arrives here as the
                // generic type definition, whose arguments still take a flag each: rendering the bare arity
                // for it left those unread and shifted every annotation after them.
                // Given_ShippedAssemblies_When_EverySignaturePositionIsRead_Then_NoFlagIsMisread holds this.
                var arguments = string.Join(
                    ", ",
                    type.GetGenericArguments().Select(argument => FormatType(argument, reader)));
                return baseName + "`" + arity + "[" + arguments + "]" + annotation;
            }

            return (type.FullName ?? type.Name) + Annotate(type, reader);
        }

        /// <summary>
        /// Spells the node's declared nullability the way C# does, and advances the reader past it even when
        /// nothing is spelled, since a value type's flag still occupies a position.
        /// </summary>
        private static string Annotate(Type type, NullableAnnotationProbe.AnnotationReader reader)
        {
            if (reader == null)
            {
                return string.Empty;
            }

            var annotation = reader.Next(type);
            if (type.IsValueType)
            {
                return string.Empty;
            }

            return annotation switch
            {
                NullableAnnotationProbe.Annotation.Nullable => "?",
                NullableAnnotationProbe.Annotation.Oblivious => "~",
                _ => string.Empty
            };
        }
    }
}
