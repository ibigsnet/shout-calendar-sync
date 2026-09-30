using System.Net;
using System.Net.Sockets;

namespace ShoutCalendar.Sync;

public static class RelayDiagnostics
{
    public static string Failure(Exception error) => error switch
    {
        RelayException { StatusCode: HttpStatusCode.TooManyRequests } => "Relay rate limit; waiting before retry",
        RelayException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden } => "Relay access refused",
        RelayException { Status: Core.RelayProtocol.Upgrade } => "Relay requires an update",
        RelayException { StatusCode: { } code } => $"Relay HTTP response {(int)code}",
        RelayException => "Relay rejected the request",
        HttpRequestException { StatusCode: { } code } => $"HTTP response {(int)code}",
        HttpRequestException => "HTTP connection failed",
        SocketException socket => $"Network error {socket.SocketErrorCode}",
        OperationCanceledException or TimeoutException => "Connection timed out",
        InvalidDataException => "Relay data could not be verified",
        IOException => "Connection or storage I/O failed",
        _ => "Sync operation failed",
    };
}
