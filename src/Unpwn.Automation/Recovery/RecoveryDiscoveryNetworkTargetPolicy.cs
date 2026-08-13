using System.Buffers.Binary;
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
    private static readonly (uint Start, uint End)[] NonPublicIpv4Ranges =
    [
        (0x00000000, 0x00FFFFFF),
        (0x0A000000, 0x0AFFFFFF),
        (0x64400000, 0x647FFFFF),
        (0x7F000000, 0x7FFFFFFF),
        (0xA9FE0000, 0xA9FEFFFF),
        (0xAC100000, 0xAC1FFFFF),
        (0xC0000000, 0xC00000FF),
        (0xC0000200, 0xC00002FF),
        (0xC0586300, 0xC05863FF),
        (0xC0A80000, 0xC0A8FFFF),
        (0xC6120000, 0xC613FFFF),
        (0xC6336400, 0xC63364FF),
        (0xCB007100, 0xCB0071FF),
        (0xE0000000, 0xFFFFFFFF),
    ];

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
            return IsPublicIpv4(address);
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

    private static bool IsPublicIpv4(IPAddress address)
    {
        var value = BinaryPrimitives.ReadUInt32BigEndian(address.GetAddressBytes());
        foreach (var (start, end) in NonPublicIpv4Ranges)
        {
            if (value >= start && value <= end)
            {
                return false;
            }
        }

        return true;
    }

    private static string NormalizeHost(string host) =>
        host.Trim().Trim('[', ']').TrimEnd('.').ToLowerInvariant();

    private static bool IsLocalHostName(string host) =>
        string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(host, "local", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".local", StringComparison.OrdinalIgnoreCase);
}
