using System;
using System.Net;

namespace QtSharp.Tests
{
    /// <summary>
    /// QHostAddress and System.Net.IPAddress: IPv4, IPv6, an IPv6 scope id, and the null address
    /// that has no IPAddress equivalent.
    /// </summary>
    [Test("test-networkinterop")]
    public static class NetworkInteropTests
    {
        public static int Run(TestContext context)
        {
            // ------------------------------------------------------------- IPv4, both directions
            IPAddress ipv4 = IPAddress.Parse("192.168.1.10");
            using (var address = new QHostAddress(ipv4))
            {
                context.Check(address.Protocol() == QAbstractSocket.NetworkLayerProtocol.IPv4Protocol,
                    $"IPAddress(IPv4) -> QHostAddress has the IPv4 protocol ({address.Protocol()})");
                context.Check(address.ToString() == "192.168.1.10",
                    $"and its dotted-quad text ({address})");
                context.Check(address.ToIPAddress().Equals(ipv4),
                    $"and IPAddress -> QHostAddress -> IPAddress round-trips ({address.ToIPAddress()})");
            }

            using (var address = new QHostAddress("10.0.0.1"))
            {
                context.Check(address.ToIPAddress().Equals(IPAddress.Parse("10.0.0.1")),
                    "QHostAddress -> IPAddress, IPv4");
            }

            // ------------------------------------------------------------- IPv6, both directions
            IPAddress ipv6 = IPAddress.Parse("2001:db8::1");
            using (var address = new QHostAddress(ipv6))
            {
                context.Check(address.Protocol() == QAbstractSocket.NetworkLayerProtocol.IPv6Protocol,
                    $"IPAddress(IPv6) -> QHostAddress has the IPv6 protocol ({address.Protocol()})");
                context.Check(address.ToString() == "2001:db8::1",
                    $"and its compressed IPv6 text ({address})");
                context.Check(address.ToIPAddress().Equals(ipv6),
                    $"and IPAddress -> QHostAddress -> IPAddress round-trips ({address.ToIPAddress()})");
            }

            // ------------------------------------------------------------- IPv6 scope id
            IPAddress scoped = new IPAddress(IPAddress.Parse("fe80::1").GetAddressBytes(), 12);
            using (var address = new QHostAddress(scoped))
            {
                context.Check(address.ScopeId() == "12",
                    $"the IPv6 scope id crosses to QHostAddress ({address.ScopeId()})");
                IPAddress back = address.ToIPAddress();
                context.Check(back.Equals(scoped) && back.ScopeId == 12,
                    $"and back to IPAddress, scope id included ({back}, scope {back.ScopeId})");
            }

            // ------------------------------------------------------------- special addresses
            using (var address = new QHostAddress(QHostAddress.SpecialAddress.Null))
            {
                context.Check(address.IsNull(), "SpecialAddress.Null is the null QHostAddress");
                try
                {
                    IPAddress converted = address.ToIPAddress();
                    context.Check(false, $"SpecialAddress.Null should not convert (got {converted})");
                }
                catch (InvalidOperationException)
                {
                    context.Check(true, "SpecialAddress.Null -> IPAddress throws InvalidOperationException");
                }
            }

            using (var address = new QHostAddress(QHostAddress.SpecialAddress.AnyIPv4))
            {
                context.Check(address.ToIPAddress().Equals(IPAddress.Any),
                    $"SpecialAddress.AnyIPv4 becomes IPAddress.Any ({address.ToIPAddress()})");
            }

            // ------------------------------------------------------------- invalid input
            try
            {
                using var address = new QHostAddress((IPAddress)null);
                context.Check(false, "a null IPAddress should not convert to QHostAddress");
            }
            catch (ArgumentNullException)
            {
                context.Check(true, "a null IPAddress throws ArgumentNullException");
            }

            return context.ExitCode;
        }
    }
}
