using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;

namespace OpenPort.Net.Internal;

internal sealed class SoapClient
{
    private readonly HttpClient _httpClient;

    public SoapClient(TimeSpan timeout)
    {
        _httpClient = new HttpClient { Timeout = timeout };
    }

    public async Task<XDocument> InvokeAsync(
        Uri controlUri,
        string serviceType,
        string action,
        IEnumerable<KeyValuePair<string, string>> arguments,
        CancellationToken cancellationToken)
    {
        var body = BuildEnvelope(serviceType, action, arguments);
        using var request = new HttpRequestMessage(HttpMethod.Post, controlUri)
        {
            Content = new StringContent(body, Encoding.UTF8, "text/xml")
        };
        request.Headers.TryAddWithoutValidation("SOAPACTION", $"\"{serviceType}#{action}\"");
        request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse("text/xml; charset=\"utf-8\"");

        using var response = await _httpClient.SendAsync(request, cancellationToken).ConfigureAwait(false);
        var content = await response.Content.ReadAsStringAsync().ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            throw new UpnpSoapException(response.StatusCode, TryReadErrorCode(content), content);
        }

        return XmlUtils.Parse(content);
    }

    private static string BuildEnvelope(string serviceType, string action, IEnumerable<KeyValuePair<string, string>> arguments)
    {
        var builder = new StringBuilder();
        builder.Append("<?xml version=\"1.0\"?>");
        builder.Append("<s:Envelope xmlns:s=\"http://schemas.xmlsoap.org/soap/envelope/\" s:encodingStyle=\"http://schemas.xmlsoap.org/soap/encoding/\">");
        builder.Append("<s:Body>");
        builder.Append('<').Append("u:").Append(action).Append(" xmlns:u=\"").Append(SecurityElementEscape(serviceType)).Append("\">");

        foreach (var argument in arguments)
        {
            builder.Append('<').Append(argument.Key).Append('>')
                .Append(SecurityElementEscape(argument.Value))
                .Append("</").Append(argument.Key).Append('>');
        }

        builder.Append("</u:").Append(action).Append('>');
        builder.Append("</s:Body></s:Envelope>");
        return builder.ToString();
    }

    private static int? TryReadErrorCode(string content)
    {
        try
        {
            var document = XmlUtils.Parse(content);
            var errorCode = document.Descendants().FirstOrDefault(e => e.Name.LocalName == "errorCode")?.Value;
            return int.TryParse(errorCode, out var code) ? code : null;
        }
        catch (Exception ex) when (ex is System.Xml.XmlException or InvalidOperationException)
        {
            return null;
        }
    }

    private static string SecurityElementEscape(string value) =>
        System.Security.SecurityElement.Escape(value) ?? "";
}

internal sealed class UpnpSoapException : Exception
{
    public UpnpSoapException(System.Net.HttpStatusCode statusCode, int? errorCode, string responseBody)
        : base($"UPnP SOAP request failed with HTTP {(int)statusCode}. Error code: {errorCode?.ToString() ?? "unknown"}.")
    {
        StatusCode = statusCode;
        ErrorCode = errorCode;
        ResponseBody = responseBody;
    }

    public System.Net.HttpStatusCode StatusCode { get; }
    public int? ErrorCode { get; }
    public string ResponseBody { get; }
}
