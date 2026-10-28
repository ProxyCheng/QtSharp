using System;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace QtSharp.Tests
{
    /// <summary>
    /// The XML seam between Qt and .NET: System.Xml produces a document that QXmlStreamReader
    /// reads, and QXmlStreamWriter produces one that XDocument parses. .NET's own XML behaviour is
    /// not under test here - only the hand-off (encoding, stream, element names, text).
    /// </summary>
    [Test("test-xmlseam")]
    public static class XmlSeamTests
    {
        public static int Run(TestContext context)
        {

            // ---- System.Xml -> QXmlStreamReader -----------------------------------------
            var document = new XDocument(
                new XElement("dashboard",
                    new XAttribute("theme", "dark"),
                    new XElement("panel", new XAttribute("id", "1"), "hello")));
            byte[] xml = Encoding.UTF8.GetBytes(document.ToString(SaveOptions.DisableFormatting));

            using (var memory = new MemoryStream(xml))
            using (var device = new StreamIODevice(memory))
            using (var reader = new QXmlStreamReader(device))
            {
                string rootName = null, text = null;
                while (!reader.AtEnd())
                {
                    var token = reader.ReadNext();
                    if (token == QXmlStreamReader.TokenType.StartElement)
                    {
                        if (rootName == null)
                            rootName = reader.Name();
                        if (reader.Name() == "panel")
                            text = reader.ReadElementText();
                    }
                }
                context.Check(rootName == "dashboard", $"QXmlStreamReader reads System.Xml's root ('{rootName}')");
                context.Check(text == "hello", $"the element text arrives through the Qt reader ('{text}')");
            }

            // ---- QXmlStreamWriter -> System.Xml -----------------------------------------
            using (var buffer = new QBuffer())
            {
                buffer.Open(QIODeviceBase.OpenMode.WriteOnly);
                using (var writer = new QXmlStreamWriter(buffer))
                {
                    writer.WriteStartDocument();
                    writer.WriteStartElement("dashboard");
                    writer.WriteAttribute("theme", "dark");
                    writer.WriteStartElement("panel");
                    writer.WriteAttribute("id", "1");
                    writer.WriteTextElement("value", "hello");
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndDocument();
                }
                buffer.Close();

                byte[] written = buffer.Data().ToArray();
                var parsed = XDocument.Parse(Encoding.UTF8.GetString(written));
                context.Check(parsed.Root?.Name.LocalName == "dashboard" &&
                      parsed.Root?.Attribute("theme")?.Value == "dark",
                    "System.Xml parses QXmlStreamWriter's root and attribute");
                context.Check(parsed.Root?.Element("panel")?.Element("value")?.Value == "hello",
                    "System.Xml parses the nested element QXmlStreamWriter wrote");
            }

            return context.ExitCode;
        }
    }
}
