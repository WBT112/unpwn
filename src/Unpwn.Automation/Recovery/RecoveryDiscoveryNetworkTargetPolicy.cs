using System.Net;
using System.Net.Sockets;

namespace Unpwn.Automation.Recovery;

public interface IRecoveryNetworkTargetPolicy
{
    ValueTask<bool> IsAllowedAsync(Uri destination, CancellationToken cancellationToken);
}

public interface IRecoveryDnsResolver
{
    ValueTask<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

public sealed class SystemRecoveryDnsResolver : IRecoveryDnsResolver
{
    public async ValueTask<IPAddress[]> ResolveAsync(
        string host,
        CancellationToken cancellationToken) =>
        await Dns.GetHostAddressesAsync(host, cancellationToken);
}

public sealed class PublicRecoveryNetworkTargetPolicy(IRecoveryDnsResolver resolver)
    : IRecoveryNetworkTargetPolicy
{
    private readonly IRecoveryDnsResolver _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));

    public static PublicRecoveryNetworkTargetPolicy CreateDefault() =>
        new(new SystemRecoveryDnsResolver());

    public async ValueTask<bool> IsAllowedAsync(
        Uri destination,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.IsAbsoluteUri ||
            !string.Equals(destination.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrWhiteSpace(destination.Host))
        {
            return false;
        }

        var host = NormalizeHost(destination.Host);
        if (host.Length == 0 || IsLocalHostName(host))
        {
            return false;
        }

        if (IPAddress.TryParse(host, out var literal))
        {
            return IsPubliclyRoutable(literal);
        }

        IPAddress[] addresses;
        try
        {
            addresses = await _resolver.ResolveAsync(host, cancellationToken);
        }
        catch (SocketException)
        {
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }

        return addresses.Length > 0 && addresses.All(IsPubliclyRoutable);
    }

    public static bool IsPubliclyRoutable(IPAddress address)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            var first = bytes[0];
            var second = bytes[1];
            var third = bytes[2];
            return first is not (0 or 10 or 127) &&
                   first < 224 &&
                   !(first == 100 && second is >= 64 and <= 127) &&
                   !(first == 169 && second == 254) &&
                   !(first == 172 && second is >= 16 and <= 31) &&
                   !(first == 192 && second == 168) &&
                   !(first == 192 && second == 0 && third is 0 or 2) &&
                   !(first == 192 && second == 88 && third == 99) &&
                   !(first == 198 && second is 18 or 19) &&
                   !(first == 198 && second == 51 && third == 100) &&
                   !(first == 203 && second == 0 && third == 113);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6 ||
            address.Equals(IPAddress.IPv6Any) ||
            address.Equals(IPAddress.IPv6Loopback) ||
            address.IsIPv6LinkLocal ||
            address.IsIPv6Multicast ||
            address.IsIPv6SiteLocal)
        {
            return false;
        }

        var ipv6 = address.GetAddressBytes();
        if ((ipv6[0] & 0xfe) == 0xfc)
        {
            return false;
        }

        if (ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0d && ipv6[3] == 0xb8)
        {
            return false;
        }

        return !ipv6.Take(12).All(value => value == 0);
    }

    private static string NormalizeHost(string host) =>
        host.Trim().Trim('[', ']').TrimEnd('.').ToLowerInvariant();

    private static bool IsLocalHostName(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "local", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
}
