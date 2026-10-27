using System;
using System.Security.Cryptography;
using System.Text;

namespace QtSharp.QtCore
{
    /// <summary>
    /// The version nibble of a UUID (RFC 9562). <see cref="UuidExtensions.Version"/> returns it;
    /// <see cref="Random"/> is what <see cref="Guid.NewGuid"/> produces and <see cref="UnixEpoch"/>
    /// what <see cref="Guid.CreateVersion7"/> does.
    /// </summary>
    public enum UuidVersion
    {
        VerUnknown = -1,
        Time = 1,
        EmbeddedPOSIX = 2,
        Md5 = 3,
        Name = 3,
        Random = 4,
        Sha1 = 5,
        UnixEpoch = 7,
    }

    /// <summary>
    /// The UUID variant (RFC 9562 section 4.1). <see cref="RFC4122"/> is the one every
    /// <see cref="Guid"/> produced by the BCL carries; Qt's QUuid spells this value DCE.
    /// </summary>
    public enum UuidVariant
    {
        VarUnknown = -1,
        NCS = 0,
        RFC4122 = 2,
        Microsoft = 6,
        Reserved = 7,
    }

    /// <summary>
    /// The part of Qt's QUuid API that <see cref="Guid"/> does not already have, as extensions on
    /// Guid - the same treatment QString gets: a QUuid is not a type in this binding, it is the
    /// BCL value.
    /// <para>
    /// A QUuid is 16 bytes and a copy owns nothing, so every Qt signature that takes or returns one
    /// surfaces as <see cref="Guid"/>, and the generated glue passes the same 16 bytes over the
    /// ABI. The value semantics are Guid's: value equality, no disposal, usable as a key.
    /// </para>
    /// <para>
    /// What is added here is only what the BCL lacks: the RFC 4122 name-based UUIDs (v3/v5), the
    /// version/variant introspection, and the network-order byte sequence. Construction, parsing
    /// and v4/v7 stay the BCL's: <see cref="Guid.Parse(string)"/>,
    /// <see cref="Guid.TryParse(string, out Guid)"/>, <see cref="Guid.NewGuid"/>, and
    /// <see cref="Guid.CreateVersion7"/>.
    /// </para>
    /// <para>
    /// Two Qt semantics cannot be carried: a null QUuid is <see cref="Guid.Empty"/> (so "null" and
    /// the zero UUID are the same value), and an unparseable string fails <c>TryParse</c> instead of
    /// becoming a silent null QUuid.
    /// </para>
    /// <para>
    /// These are C# 14 extension members, so a call site must be compiled with the .NET 10 compiler
    /// (the default for a project targeting net10.0).
    /// </para>
    /// </summary>
    public static class UuidExtensions
    {
        extension(Guid value)
        {
            /// <summary>
            /// A version-3 (MD5) UUID derived from <paramref name="ns"/> and the raw name bytes -
            /// Qt's <c>QUuid::createUuidV3</c>.
            /// </summary>
            public static Guid CreateV3(Guid ns, byte[] name)
                => CreateNameBased(ns, name ?? Array.Empty<byte>(), sha1: false);

            /// <summary>A version-3 (MD5) UUID whose name is the UTF-8 bytes of <paramref name="name"/>.</summary>
            public static Guid CreateV3(Guid ns, string name)
                => CreateV3(ns, Encoding.UTF8.GetBytes(name ?? string.Empty));

            /// <summary>
            /// A version-5 (SHA-1) UUID derived from <paramref name="ns"/> and the raw name bytes -
            /// Qt's <c>QUuid::createUuidV5</c>.
            /// </summary>
            public static Guid CreateV5(Guid ns, byte[] name)
                => CreateNameBased(ns, name ?? Array.Empty<byte>(), sha1: true);

            /// <summary>A version-5 (SHA-1) UUID whose name is the UTF-8 bytes of <paramref name="name"/>.</summary>
            public static Guid CreateV5(Guid ns, string name)
                => CreateV5(ns, Encoding.UTF8.GetBytes(name ?? string.Empty));

            /// <summary>
            /// The UUID whose RFC 4122 byte sequence is <paramref name="bytes"/> (16 bytes, network
            /// order) - Qt's <c>QUuid::fromRfc4122</c>. The inverse of
            /// <see cref="ToRfc4122"/>; <c>new Guid(bytes, bigEndian: true)</c> is the same
            /// conversion with a length check of its own.
            /// </summary>
            public static Guid FromRfc4122(ReadOnlySpan<byte> bytes)
            {
                if (bytes.Length != 16)
                    throw new ArgumentException("A UUID is exactly 16 bytes.", nameof(bytes));
                return new Guid(bytes, bigEndian: true);
            }

            /// <summary>
            /// <c>QUuid::version</c>: the version nibble, or <see cref="UuidVersion.VerUnknown"/>
            /// when the variant is not RFC 4122.
            /// </summary>
            public UuidVersion Version()
            {
                Span<byte> bytes = stackalloc byte[16];
                value.TryWriteBytes(bytes, bigEndian: true, out _);
                var version = (UuidVersion)(bytes[6] >> 4);
                return IsKnownVersion(version) && (bytes[8] & 0xC0) == 0x80
                    ? version
                    : UuidVersion.VerUnknown;
            }

            /// <summary><c>QUuid::variant</c>.</summary>
            public UuidVariant Variant()
            {
                Span<byte> bytes = stackalloc byte[16];
                value.TryWriteBytes(bytes, bigEndian: true, out _);
                byte variant = (byte)(bytes[8] & 0xE0);
                if (variant < 0x80)
                    return value == Guid.Empty ? UuidVariant.VarUnknown : UuidVariant.NCS;
                if (variant < 0xC0)
                    return UuidVariant.RFC4122;
                return (UuidVariant)(variant >> 5);
            }

            /// <summary>
            /// <c>QUuid::toRfc4122</c>: the 16 bytes in network order, which is what a database or
            /// protocol storing UUIDs expects (unlike <see cref="Guid.ToByteArray()"/>, whose first
            /// three fields are little-endian).
            /// </summary>
            public byte[] ToRfc4122() => value.ToByteArray(bigEndian: true);
        }

        // RFC 4122 section 4.3: hash the namespace in network order followed by the name, keep the
        // first 16 bytes, then stamp the version and the RFC 4122 variant bits over them.
        static Guid CreateNameBased(Guid ns, byte[] name, bool sha1)
        {
            byte[] data = new byte[16 + name.Length];
            ns.TryWriteBytes(data, bigEndian: true, out _);
            name.CopyTo(data, 16);
            byte[] hash = sha1 ? SHA1.HashData(data) : MD5.HashData(data);
            hash[6] = (byte)((hash[6] & 0x0F) | (sha1 ? 0x50 : 0x30));
            hash[8] = (byte)((hash[8] & 0x3F) | 0x80);
            return new Guid(hash.AsSpan(0, 16), bigEndian: true);
        }

        static bool IsKnownVersion(UuidVersion version)
            => version is UuidVersion.Time or UuidVersion.EmbeddedPOSIX or UuidVersion.Md5
                or UuidVersion.Random or UuidVersion.Sha1 or UuidVersion.UnixEpoch;
    }
}
