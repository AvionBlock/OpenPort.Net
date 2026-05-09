using System.Xml;
using System.Xml.Linq;

namespace OpenPort.Net.Internal;

internal static class XmlUtils
{
    public static XDocument Parse(string xml)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };

        using var reader = XmlReader.Create(new StringReader(xml), settings);
        return XDocument.Load(reader, LoadOptions.None);
    }
}
