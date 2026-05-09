using OpenPort.Net.Internal;
using OpenPort.Net.Models;

namespace OpenPort.Net.Tests;

public class UpnpErrorMapperTests
{
    [Theory]
    [InlineData(402, OpenPortStatus.InvalidRequest)]
    [InlineData(606, OpenPortStatus.Unauthorized)]
    [InlineData(718, OpenPortStatus.Conflict)]
    [InlineData(725, OpenPortStatus.NotSupported)]
    [InlineData(728, OpenPortStatus.NoResources)]
    [InlineData(729, OpenPortStatus.Conflict)]
    public void MapSoapError_ReturnsExpectedStatus(int errorCode, OpenPortStatus expected)
    {
        Assert.Equal(expected, UpnpErrorMapper.MapSoapError(errorCode));
    }
}
