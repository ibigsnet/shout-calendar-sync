using System.Net;
using System.Net.Sockets;

namespace ShoutCalendar.Sync;

public static class RelayFailover
{
    public static bool IsAvailabilityFailure(Exception error) => error switch
    {
        RelayException relay => relay.StatusCode is HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout,
        InvalidDataException => false,
        HttpRequestException http => http.StatusCode is null or HttpStatusCode.BadGateway or HttpStatusCode.ServiceUnavailable or HttpStatusCode.GatewayTimeout,
        SocketException or EndOfStreamException or OperationCanceledException or TimeoutException => true,
        IOException => true,
        _ => false,
    };
}
