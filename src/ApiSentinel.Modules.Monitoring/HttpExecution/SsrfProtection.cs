using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;

namespace ApiSentinel.Modules.Monitoring.HttpExecution;

public interface IDnsAddressResolver
{
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken);
}

internal sealed class SystemDnsAddressResolver : IDnsAddressResolver
{
    public async Task<IPAddress[]> ResolveAsync(
        string host,
        CancellationToken cancellationToken)
    {
        if (IPAddress.TryParse(host, out var literalAddress))
        {
            return [literalAddress];
        }

        return await Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}

internal interface ISsrfTargetValidator
{
    Task<IReadOnlyList<IPAddress>> ResolveAllowedAddressesAsync(
        string host,
        CancellationToken cancellationToken);
}

internal sealed class SsrfTargetValidator : ISsrfTargetValidator
{
    private readonly IDnsAddressResolver _resolver;
    private readonly HashSet<string> _developmentInternalHosts;

    public SsrfTargetValidator(
        IDnsAddressResolver resolver,
        IOptions<NetworkSecurityOptions> options)
    {
        _resolver = resolver;
        _developmentInternalHosts = options.Value.DevelopmentInternalHosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(NormalizeHost)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public async Task<IReadOnlyList<IPAddress>> ResolveAllowedAddressesAsync(
        string host,
        CancellationToken cancellationToken)
    {
        var normalizedHost = NormalizeHost(host);
        var addresses = await _resolver.ResolveAsync(normalizedHost, cancellationToken);
        if (addresses.Length == 0)
        {
            throw new UnsafeTargetException("O host de destino não possui endereços IP.");
        }

        var isDevelopmentInternalHost = _developmentInternalHosts.Contains(normalizedHost);
        foreach (var address in addresses)
        {
            var normalizedAddress = NormalizeAddress(address);
            if (IpAddressPolicy.IsAlwaysBlocked(normalizedAddress) ||
                (!isDevelopmentInternalHost && IpAddressPolicy.IsNonPublic(normalizedAddress)))
            {
                throw new UnsafeTargetException(
                    "Destino bloqueado pela política SSRF: o host resolve para uma rede não pública.");
            }
        }

        return addresses.Select(NormalizeAddress).Distinct().ToArray();
    }

    private static string NormalizeHost(string host) => host.Trim().TrimEnd('.');

    private static IPAddress NormalizeAddress(IPAddress address) =>
        address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
}

internal static class IpAddressPolicy
{
    public static bool IsAlwaysBlocked(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 0 ||
                   bytes[0] >= 224 ||
                   (bytes[0] == 169 && bytes[1] == 254);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return true;
        }

        var ipv6 = address.GetAddressBytes();
        return address.Equals(IPAddress.IPv6None) ||
               address.IsIPv6Multicast ||
               address.IsIPv6LinkLocal ||
               (ipv6[0] == 0x20 && ipv6[1] == 0x01 && ipv6[2] == 0x0d && ipv6[3] == 0xb8);
    }

    public static bool IsNonPublic(IPAddress address)
    {
        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return bytes[0] == 10 ||
                   bytes[0] == 127 ||
                   (bytes[0] == 100 && bytes[1] is >= 64 and <= 127) ||
                   (bytes[0] == 172 && bytes[1] is >= 16 and <= 31) ||
                   (bytes[0] == 192 && bytes[1] == 168) ||
                   (bytes[0] == 192 && bytes[1] == 0 && bytes[2] is 0 or 2) ||
                   (bytes[0] == 198 && bytes[1] is 18 or 19) ||
                   (bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) ||
                   (bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
        }

        if (address.AddressFamily != AddressFamily.InterNetworkV6)
        {
            return true;
        }

        var bytesV6 = address.GetAddressBytes();
        return address.Equals(IPAddress.IPv6Loopback) ||
               (bytesV6[0] & 0xfe) == 0xfc;
    }
}

internal sealed class UnsafeTargetException(string message) : Exception(message);
