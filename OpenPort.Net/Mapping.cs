using System.Net;

namespace OpenPort.Net;

public struct Mapping(string description, IPEndPoint privateEndpoint, IPEndPoint publicEndPoint, TimeSpan lifetime)
{
    public string Description = description;
    public IPEndPoint PrivateEndpoint = privateEndpoint;
    public IPEndPoint PublicEndPoint = publicEndPoint;
    public TimeSpan Lifetime = lifetime;
}