using OpenPort.Net;
using OpenPort.Net.Models;

var internalPort = args.Length > 0 && int.TryParse(args[0], out var parsedInternalPort)
    ? parsedInternalPort
    : 19132;
var externalPort = args.Length > 1 && int.TryParse(args[1], out var parsedExternalPort)
    ? parsedExternalPort
    : internalPort;
var protocol = args.Length > 2 && string.Equals(args[2], "tcp", StringComparison.OrdinalIgnoreCase)
    ? PortProtocol.Tcp
    : PortProtocol.Udp;

var client = new OpenPortClient();
await using var lease = await client.OpenLeaseAsync(new PortMapping
{
    InternalPort = internalPort,
    ExternalPort = externalPort,
    Protocol = protocol,
    Description = "OpenPort.Net sample",
    Lifetime = TimeSpan.FromHours(1)
});

Console.WriteLine($"Status: {lease.Result.Status}");
Console.WriteLine($"Provider: {lease.Result.Provider}");
Console.WriteLine($"External address: {lease.Result.ExternalAddress?.ToString() ?? "unknown"}");
Console.WriteLine($"External port: {lease.Result.ExternalPort?.ToString() ?? lease.Mapping.ExternalPort.ToString()}");
Console.WriteLine("Press Enter to close the mapping.");
Console.ReadLine();
