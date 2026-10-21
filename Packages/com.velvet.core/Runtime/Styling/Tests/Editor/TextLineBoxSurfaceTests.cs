using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine.UIElements;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins the public surface the single overline rule rests on. The carriers are the text handle,
    /// its line info, and a public <see cref="TextElement"/> member that would return them.
    /// </summary>
    [TestFixture]
    internal sealed class TextLineBoxSurfaceTests
    {
        private static readonly (Assembly Assembly, string Name)[] Carriers =
        {
            (typeof(TextElement).Assembly, "UnityEngine.UIElements.UITKTextHandle"),
            (typeof(UnityEngine.TextCore.Text.FontAsset).Assembly, "UnityEngine.TextCore.Text.TextHandle"),
            (typeof(UnityEngine.TextCore.Text.FontAsset).Assembly, "UnityEngine.TextCore.Text.TextInfo"),
            (typeof(UnityEngine.TextCore.Text.FontAsset).Assembly, "UnityEngine.TextCore.Text.LineInfo"),
        };

        // GREEN_ON_BASE(characterization): Unity's text line boxes are already off the public surface on the base.
        [Test]
        public void Given_TheTextElementPublicSurface_When_LineBoxCarriersAreRead_Then_NoneAreExposed()
        {
            // Act
            var exposure = DescribeLineBoxExposure();

            // Assert
            Assert.That(exposure, Is.EqualTo(string.Empty));
        }

        private static string DescribeLineBoxExposure()
        {
            var found = new List<string>();
            foreach (var (assembly, name) in Carriers)
            {
                var type = assembly.GetType(name);
                if (type == null)
                {
                    found.Add(name + " missing");
                }
                else if (type.IsPublic)
                {
                    found.Add(name + " public");
                }
            }

            var handle = typeof(TextElement).GetProperty(
                "uitkTextHandle", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (handle == null)
            {
                found.Add("TextElement.uitkTextHandle missing");
            }
            else if (handle.GetMethod?.IsPublic == true)
            {
                found.Add("TextElement.uitkTextHandle public");
            }

            CollectLineBoxMembers(typeof(TextElement), found);
            foreach (var iface in typeof(TextElement).GetInterfaces())
            {
                if (iface.IsPublic)
                {
                    CollectLineBoxMembers(iface, found);
                }
            }
            return string.Join(", ", found);
        }

        private static void CollectLineBoxMembers(Type type, List<string> found)
        {
            const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static
                | BindingFlags.DeclaredOnly;
            foreach (var member in type.GetMembers(flags))
            {
                if (member is MethodBase { IsSpecialName: true })
                {
                    continue;
                }
                if (IsLineBoxMember(member.Name) || ReturnTypeIsLineBox(member))
                {
                    found.Add(type.Name + "." + member.Name);
                }
            }
        }

        private static bool IsLineBoxMember(string name)
            => name.Contains("LineInfo", StringComparison.Ordinal)
                || name.Contains("lineCount", StringComparison.Ordinal)
                || name.Contains("LineCount", StringComparison.Ordinal)
                || name.StartsWith("GetLine", StringComparison.Ordinal)
                || name.Contains("LineNumber", StringComparison.Ordinal)
                || name.Contains("TextInfo", StringComparison.Ordinal)
                || name.Contains("textInfo", StringComparison.Ordinal);

        private static bool ReturnTypeIsLineBox(MemberInfo member)
        {
            var returned = member switch
            {
                PropertyInfo property => property.PropertyType,
                MethodInfo method => method.ReturnType,
                _ => null,
            };
            if (returned == null)
            {
                return false;
            }
            if (returned.IsArray)
            {
                returned = returned.GetElementType();
            }
            var name = returned?.Name;
            return name is "LineInfo" or "TextInfo" or "TextHandle" or "UITKTextHandle";
        }
    }
}
