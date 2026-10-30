using System.Net;

// The source-side spelling of the QHostAddress bridge: `address.ToQHostAddress()` instead of
// `new QHostAddress(address)`. The conversion carries an IPv6 scope id and throws through the
// constructor when the address cannot be represented; the returned wrapper owns the native
// object and is the caller's to dispose.
namespace QtSharp.QtNetwork
{
    /// <summary>BCL -> Qt conversions for the QtNetwork types.</summary>
    public static class BclToQtExtensions
    {
        /// <summary>A QHostAddress for the IPv4/IPv6 address, carrying its scope id.</summary>
        public static QHostAddress ToQHostAddress(this IPAddress value) => new QHostAddress(value);
    }
}
