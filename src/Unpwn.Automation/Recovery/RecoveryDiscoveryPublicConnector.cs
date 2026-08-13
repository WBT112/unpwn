using System.Net;
using System.Net.Sockets;

namespace Unpwn.Automation.Recovery;

internal static class RecoveryDiscoveryPublicConnector
{
    internal static async ValueTask<Stream> ConnectAsync(
        SocketsHttpConnectionContext context,
        IRecoveryDnsResolver resolver,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(resolver);
        cancellationToken.ThrowIfCancellationRequested();

        var host = context.DnsEndPoint.Host.Trim().Trim('[', ']').TrimEnd('.');
        IPAddress[] addresses;
        if (IPAddress.TryParse(host, out var literal))
        {
            addresses = [literal];
        }
        else
        {
            addresses = await resolver.ResolveAsync(host, cancellationToken);
        }

        if (addresses.Length == 0 ||
            addresses.Any(address => !PublicRecoveryNetworkTargetPolicy.IsPubliclyRoutable(address)))
        {
            throw new HttpRequestException("The recovery discovery network target is not allowed.");
        }

        SocketException? lastFailure = null;
        foreach (var address in addresses.Distinct())
        {
            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(
                    new IPEndPoint(address, context.DnsEndPoint.Port),
                    cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (OperationCanceledException)
            {
                socket.Dispose();
                throw;
            }
            catch (SocketException exception)
            {
                socket.Dispose();
                lastFailure = exception;
            }
        }

        throw new HttpRequestException("The recovery discovery connection failed.", lastFailure);
    }
}
