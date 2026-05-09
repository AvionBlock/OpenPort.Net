using System.Xml;
using OpenPort.Net.Internal;

namespace OpenPort.Net.Tests;

public class XmlUtilsTests
{
    [Fact]
    public void Parse_RejectsDtd()
    {
        const string xml = """
        <!DOCTYPE root [<!ENTITY xxe SYSTEM "file:///etc/passwd">]>
        <root>&xxe;</root>
        """;

        Assert.Throws<XmlException>(() => XmlUtils.Parse(xml));
    }
}
