using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using NUnit.Framework;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the shipped runtime assembly's public and protected member surface to a checked-in file so every
    /// addition and removal is a reviewable diff. The CHANGELOG's [Unreleased] breaking entries are written
    /// down by hand; nothing else compares two commits.
    /// </summary>
    [TestFixture]
    internal sealed class PublicApiSurfaceTests
    {
        // Regeneration is opt-in so a normal test run never rewrites the pin file.
        private const string UpdateEnvironmentVariable = "VELVET_UPDATE_PUBLIC_API";

        private static readonly string PublicApiPath =
            Path.GetFullPath("Packages/com.velvet.core/PublicAPI.txt");

        [Test]
        public void Given_VelvetRuntimeAssembly_When_PublicApiSurfaceIsRendered_Then_ItMatchesPublicApiTxt()
        {
            // Arrange
            var rendered = PublicApiSurface.Render(typeof(V).Assembly).ToArray();

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

    internal static class PublicApiSurface
    {
        private const BindingFlags MemberFlags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
            | BindingFlags.Static | BindingFlags.DeclaredOnly;

        public static IReadOnlyList<string> Render(Assembly assembly)
        {
            var lines = new List<string>();
            foreach (var type in assembly.GetTypes()
                         .Where(IsSurfaceType)
                         .OrderBy(type => type.FullName, StringComparer.Ordinal))
            {
                lines.Add(RenderType(type));
                lines.AddRange(RenderMembers(type));
            }

            lines.Sort(StringComparer.Ordinal);
            return lines;
        }

        private static bool IsSurfaceType(Type type)
        {
            if (!type.IsPublic && !type.IsNestedPublic)
            {
                return false;
            }

            return !IsCompilerGenerated(type);
        }

        private static IEnumerable<string> RenderMembers(Type type)
        {
            foreach (var constructor in type.GetConstructors(MemberFlags))
            {
                var line = RenderConstructor(type, constructor);
                if (line != null)
                {
                    yield return line;
                }
            }

            foreach (var method in type.GetMethods(MemberFlags))
            {
                var line = RenderMethod(type, method);
                if (line != null)
                {
                    yield return line;
                }
            }

            foreach (var property in type.GetProperties(MemberFlags))
            {
                var line = RenderProperty(type, property);
                if (line != null)
                {
                    yield return line;
                }
            }

            foreach (var field in type.GetFields(MemberFlags))
            {
                var line = RenderField(type, field);
                if (line != null)
                {
                    yield return line;
                }
            }

            foreach (var eventInfo in type.GetEvents(MemberFlags))
            {
                var line = RenderEvent(type, eventInfo);
                if (line != null)
                {
                    yield return line;
                }
            }
        }

        private static string RenderType(Type type) => "type " + FormatType(type);

        private static string RenderConstructor(Type declaringType, ConstructorInfo constructor)
        {
            if (!IsVisibleMethod(constructor) || IsCompilerGenerated(constructor))
            {
                return null;
            }

            return Prefix("ctor", constructor)
                   + FormatType(declaringType)
                   + ".ctor("
                   + FormatParameters(constructor.GetParameters())
                   + "): System.Void";
        }

        private static string RenderMethod(Type declaringType, MethodInfo method)
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
                   + FormatParameters(method.GetParameters())
                   + "): "
                   + FormatType(method.ReturnType);
        }

        private static string RenderProperty(Type declaringType, PropertyInfo property)
        {
            if (IsCompilerGenerated(property) || !HasVisibleAccessor(property, out var accessor))
            {
                return null;
            }

            var indexer = property.GetIndexParameters();
            var indexerSuffix = indexer.Length == 0
                ? string.Empty
                : "[" + FormatParameters(indexer) + "]";

            return Prefix("property", accessor)
                   + FormatType(declaringType)
                   + "."
                   + property.Name
                   + indexerSuffix
                   + ": "
                   + FormatType(property.PropertyType);
        }

        private static string RenderField(Type declaringType, FieldInfo field)
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
                   + FormatType(field.FieldType);
        }

        private static string RenderEvent(Type declaringType, EventInfo eventInfo)
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
                   + FormatType(eventInfo.EventHandlerType);
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

        private static string FormatParameters(IReadOnlyList<ParameterInfo> parameters) =>
            string.Join(", ", parameters.Select(parameter => FormatType(parameter.ParameterType)));

        private static string FormatType(Type type)
        {
            if (type.IsByRef)
            {
                return FormatType(type.GetElementType()!) + "&";
            }

            if (type.IsArray)
            {
                var rank = type.GetArrayRank();
                var suffix = rank == 1 ? "[]" : "[" + new string(',', rank - 1) + "]";
                return FormatType(type.GetElementType()!) + suffix;
            }

            if (type.IsGenericParameter)
            {
                return type.Name;
            }

            if (type.IsGenericType)
            {
                var definition = type.IsGenericTypeDefinition ? type : type.GetGenericTypeDefinition();
                var definitionName = definition.FullName ?? definition.Name;
                var tickIndex = definitionName.IndexOf('`', StringComparison.Ordinal);
                var baseName = tickIndex >= 0 ? definitionName[..tickIndex] : definitionName;
                var arity = definition.GetGenericArguments().Length;
                if (type.IsGenericTypeDefinition)
                {
                    return baseName + "`" + arity;
                }

                var arguments = string.Join(", ", type.GetGenericArguments().Select(FormatType));
                return baseName + "`" + arity + "[" + arguments + "]";
            }

            return type.FullName ?? type.Name;
        }
    }
}
