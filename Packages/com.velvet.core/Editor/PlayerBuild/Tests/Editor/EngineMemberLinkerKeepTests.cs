using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using NUnit.Framework;
using Velvet.Editor;

namespace Velvet.Tests
{
    /// <summary>
    /// Pins that the link.xml the build hands the linker keeps every <see cref="EngineMember"/>, read the way the
    /// linker reads it: a nested type after a slash, a property by its own element, a constructed type's
    /// constructor with it. Whether the linker then honours the file is measured only by a player build.
    /// </summary>
    [TestFixture]
    internal sealed class EngineMemberLinkerKeepTests
    {
        private const BindingFlags Declared = BindingFlags.Public | BindingFlags.NonPublic
            | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;

        private static IEnumerable<TestCaseData> Members()
            => EngineMember.Declared().Select(member => new TestCaseData(member).SetName(
                "Given_ADeclaredEngineMember_When_TheLinkXmlIsRead_Then_ItKeepsThatMember("
                + $"{member.TypeName.Split('.').Last()}.{member.Name.Split('.').Last()})"));

        [TestCaseSource(nameof(Members))]
        public void Given_ADeclaredEngineMember_When_TheLinkXmlIsRead_Then_ItKeepsThatMember(EngineMember member)
        {
            // Arrange
            var kept = Kept(EngineMemberLinkerKeep.LinkXml());

            // Act
            var keeps = kept.Contains(Key(member.Assembly.GetName().Name, member.TypeName, member.Kind, member.Name));

            // Assert
            Assert.That(keeps, Is.True, $"the link.xml does not keep {member}:\n{EngineMemberLinkerKeep.LinkXml()}");
        }

        [Test]
        public void Given_ABuild_When_TheLinkerAsksForAdditionalLinkXml_Then_TheFileItGetsHoldsTheKeep()
        {
            // Arrange — from no directory, since the build's Temp starts empty.
            var processor = new EngineMemberLinkerKeep();
            if (Directory.Exists("Temp/Velvet")) Directory.Delete("Temp/Velvet", true);

            // Act
            var path = processor.GenerateAdditionalLinkXmlFile(null, null);

            // Assert
            Assert.That((Path.IsPathRooted(path), XDocument.Load(path).ToString()),
                Is.EqualTo((true, EngineMemberLinkerKeep.LinkXml().ToString())));
        }

        // What each element keeps, keyed by the reflection names of what it resolves to in this editor; an element
        // that names nothing here keeps nothing.
        private static HashSet<string> Kept(XDocument linkXml)
        {
            var kept = new HashSet<string>();
            foreach (var assemblyElement in linkXml.Root!.Elements("assembly"))
            {
                var assemblyName = (string)assemblyElement.Attribute("fullname");
                var assembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(a => a.GetName().Name == assemblyName);
                foreach (var typeElement in assemblyElement.Elements("type"))
                {
                    var type = LinkerType(assembly, (string)typeElement.Attribute("fullname"));
                    if (type == null) continue;

                    foreach (var memberElement in typeElement.Elements())
                    {
                        var name = (string)memberElement.Attribute("name");
                        var kind = memberElement.Name.LocalName switch
                        {
                            "field" when type.GetField(name, Declared) != null => MemberTypes.Field,
                            "property" when type.GetProperty(name, Declared) != null => MemberTypes.Property,
                            "method" when name == ".ctor" && type.GetConstructor(Type.EmptyTypes) != null => MemberTypes.TypeInfo,
                            _ => (MemberTypes?)null,
                        };
                        if (kind is { } found)
                        {
                            kept.Add(Key(assemblyName, type.FullName, found, found == MemberTypes.TypeInfo ? type.FullName : name));
                        }
                    }
                }
            }

            return kept;
        }

        // The linker's spelling: the outermost type by its full name, then each nested type after a slash.
        private static Type LinkerType(Assembly assembly, string fullname)
        {
            var names = fullname.Split('/');
            var type = assembly?.GetTypes().FirstOrDefault(candidate => !candidate.IsNested && candidate.FullName == names[0]);
            foreach (var nested in names.Skip(1))
            {
                type = type?.GetNestedType(nested, BindingFlags.Public | BindingFlags.NonPublic);
            }

            return type;
        }

        private static string Key(string assembly, string type, MemberTypes kind, string name) => $"{assembly}|{type}|{kind}|{name}";
    }
}
