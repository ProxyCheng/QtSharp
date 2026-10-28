using System;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

// QHostAddress and System.Net.IPAddress both describe an IPv4 or IPv6 endpoint. They are separate
// objects with separate lifetimes, so the conversion is explicit in both directions, like the
// value-type conversions in runtime/QtCore/BclInterop.cs: a constructor and ToIPAddress(), with no
// implicit or explicit operators.
//
// IPv4 and IPv6 map one-to-one, and an IPv6 scope id (the interface index or name Qt keeps in
// QHostAddress::scopeId()) is carried with the address. QHostAddress.SpecialAddress.Null - and an
// invalid QHostAddress built from text that does not parse - has no IPAddress equivalent and
// throws InvalidOperationException. A non-numeric scope id (an interface name on Unix) cannot be
// represented by IPAddress and throws NotSupportedException; it is never dropped.
namespace QtSharp.QtNetwork
{
    public partial class QHostAddress
    {
        /// <summary>
        /// Builds a QHostAddress from a BCL IP address, IPv4 or IPv6, carrying an IPv6 scope id.
        /// </summary>
        /// <param name="address">The address to copy.</param>
        /// <exception cref="ArgumentNullException"><paramref name="address"/> is null.</exception>
        /// <exception cref="InvalidOperationException">
        /// Qt rejects the address text; this cannot happen for an address produced by
        /// <see cref="IPAddress"/> itself.
        /// </exception>
        /// <remarks>
        /// The address crosses as its <see cref="IPAddress.ToString"/> spelling, which is the form
        /// that also carries an IPv6 scope id (<c>fe80::1%12</c>). Qt parses and stores the value;
        /// no reference to the managed <see cref="IPAddress"/> is kept.
        /// </remarks>
        public QHostAddress(IPAddress address)
            : this((address ?? throw new ArgumentNullException(nameof(address))).ToString())
        {
            if (IsNull())
                throw new InvalidOperationException(
                    $"Qt's QHostAddress parser rejects the System.Net.IPAddress text '{address}'.");
        }

        /// <summary>
        /// The address as a BCL <see cref="IPAddress"/>, with its IPv6 scope id when it has one.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The QHostAddress is null/invalid (including <see cref="QHostAddress.SpecialAddress.Null"/>)
        /// or its protocol is not IPv4/IPv6.
        /// </exception>
        /// <exception cref="NotSupportedException">
        /// The address's scope id is not numeric, which <see cref="IPAddress"/> cannot represent,
        /// or Qt's address text does not parse as an IP address.
        /// </exception>
        /// <remarks>
        /// The value crosses as Qt's own <see cref="ToString"/> spelling, which is the canonical
        /// dotted-quad or compressed IPv6 form and appends a non-empty scope id after <c>%</c>.
        /// A null QHostAddress is an error rather than <see cref="IPAddress.None"/>: replacing it
        /// with 255.255.255.255 would be a silent lie.
        /// </remarks>
        public IPAddress ToIPAddress()
        {
            if (IsNull())
                throw new InvalidOperationException(
                    "The QHostAddress is null or invalid (QHostAddress.SpecialAddress.Null has no "
                    + "System.Net.IPAddress equivalent).");

            QAbstractSocket.NetworkLayerProtocol protocol = Protocol();
            if (protocol != QAbstractSocket.NetworkLayerProtocol.IPv4Protocol
                && protocol != QAbstractSocket.NetworkLayerProtocol.IPv6Protocol)
                throw new InvalidOperationException(
                    $"The QHostAddress has protocol {protocol}; System.Net.IPAddress represents "
                    + "only IPv4 and IPv6.");

            // Check the scope id before parsing: IPAddress's scope is a number, so an interface
            // name (which Qt can hold on Unix) has no representation. Reading it here makes the
            // refusal explicit instead of relying on how IPAddress.TryParse treats a name.
            string scopeId = ScopeId();
            if (scopeId.Length > 0
                && !long.TryParse(scopeId, NumberStyles.None, CultureInfo.InvariantCulture, out _))
                throw new NotSupportedException(
                    $"The QHostAddress has the IPv6 scope id '{scopeId}', but System.Net.IPAddress "
                    + "stores a numeric scope id; an interface name cannot be represented.");

            string text = ToString();
            if (IPAddress.TryParse(text, out IPAddress address))
                return address;
            throw new NotSupportedException(
                $"The QHostAddress '{text}' could not be parsed as a System.Net.IPAddress.");
        }
    }
}
