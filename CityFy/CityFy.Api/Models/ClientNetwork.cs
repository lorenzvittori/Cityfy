using System.Net;
using System.Net.Sockets;

namespace CityFy.Api.Models;

/// <summary>
/// The network a request comes from, as counted by the rate limits: an IPv4 address on its own, an
/// IPv6 address by its /64 (one home or office gets a whole /64: counting single addresses would let
/// anyone change address and start over).
/// </summary>
public sealed record ClientNetwork(string Key)
{
    private const int Ipv6PrefixBytes = 8;

    public static readonly ClientNetwork Unknown = new("unknown");

    public static ClientNetwork From(IPAddress? address)
    {
        if (address is null)
            return Unknown;
        if (address.IsIPv4MappedToIPv6)
            address = address.MapToIPv4();
        if (address.AddressFamily != AddressFamily.InterNetworkV6)
            return new ClientNetwork(address.ToString());

        var bytes = address.GetAddressBytes();
        Array.Clear(bytes, Ipv6PrefixBytes, bytes.Length - Ipv6PrefixBytes);
        return new ClientNetwork($"{new IPAddress(bytes)}/64");
    }
}
