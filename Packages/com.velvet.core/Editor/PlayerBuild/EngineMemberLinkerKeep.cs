using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.UnityLinker;

namespace Velvet.Editor
{
    /// <summary>
    /// Keeps every <see cref="EngineMember"/> through managed code stripping, which otherwise may remove an engine
    /// member Velvet reaches only by name.
    /// </summary>
    /// <remarks>
    /// A linker processor rather than a <c>link.xml</c> beside the package: in 6000.3.23f1
    /// <c>AssemblyStripper.GetLinkXmlFiles</c> collects <c>link.xml</c> files from under <c>Assets</c> only, and
    /// takes the files <see cref="IUnityLinkerProcessor"/>s return besides.
    /// </remarks>
    internal sealed class EngineMemberLinkerKeep : IUnityLinkerProcessor
    {
        private const string LinkXmlPath = "Temp/Velvet/EngineMembers.link.xml";

        public int callbackOrder => 0;

        public string GenerateAdditionalLinkXmlFile(BuildReport report, UnityLinkerBuildPipelineData data)
        {
            var path = Path.GetFullPath(LinkXmlPath);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            LinkXml(EngineMember.Declared()).Save(path);
            return path;
        }

        internal static XDocument LinkXml(IEnumerable<EngineMember> members)
            => new(new XElement("linker", members
                .GroupBy(member => member.Assembly.GetName().Name)
                .OrderBy(assembly => assembly.Key)
                .Select(assembly => new XElement("assembly", new XAttribute("fullname", assembly.Key), assembly
                    .GroupBy(member => member.TypeName)
                    .OrderBy(type => type.Key)
                    .Select(type => new XElement("type",
                        // The linker names a nested type after a slash where reflection puts a plus.
                        new XAttribute("fullname", type.Key.Replace('+', '/')),
                        new XAttribute("preserve", "nothing"),
                        type.OrderBy(member => member.Name).Select(Keep)))))));

        private static XElement Keep(EngineMember member)
        {
            if (member.Kind == MemberTypes.TypeInfo) return new XElement("method", new XAttribute("name", ".ctor"));
            // By name, which keeps every overload of it: the linker's signature form spells generic and nested types
            // differently from reflection, and a second spelling of the signature would be one more thing to drift.
            if (member.Kind == MemberTypes.Method) return new XElement("method", new XAttribute("name", member.Name));
            if (member.Kind == MemberTypes.Field) return new XElement("field", new XAttribute("name", member.Name));
            return new XElement("property", new XAttribute("name", member.Name));
        }
    }
}
