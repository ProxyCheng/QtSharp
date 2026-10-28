using System;

// QVersionNumber and System.Version both hold a dotted numeric version, but their arity differs.
// QVersionNumber stores any number of non-negative segments; Version stores exactly major and
// minor plus optional build and revision, where Build/Revision == -1 means "omitted". The
// conversion copies the segments in order:
//
//     QVersionNumber [1]        -> Version(1, 0)          (a Version must have a minor)
//     QVersionNumber [1, 2]     -> Version(1, 2)
//     QVersionNumber [1, 2, 3]  -> Version(1, 2, 3)       (Build == 3, Revision == -1)
//     QVersionNumber [1, 2, 3, 4] -> Version(1, 2, 3, 4)
//
// An empty QVersionNumber (IsNull) or one with more than four segments cannot be represented and
// throws InvalidOperationException, as does a negative segment. Version -> QVersionNumber drops a
// Build/Revision of -1 (it is the absence of a segment, not a value), so 2-, 3- and 4-segment
// versions round-trip exactly; the 1-segment QVersionNumber round-trips as [major, 0], because
// System.Version has no one-component form. QVersionNumber has no suffix field, so a suffix ("-rc1")
// is never part of this conversion: it stays in the string the caller parsed it from.
namespace QtSharp.QtCore
{
    public partial class QVersionNumber
    {
        /// <summary>Builds a QVersionNumber from a BCL <see cref="Version"/>.</summary>
        /// <param name="version">The version whose components are copied.</param>
        /// <exception cref="ArgumentNullException"><paramref name="version"/> is null.</exception>
        /// <remarks>
        /// Major and minor are always present. Build and Revision are copied only when they are
        /// not -1 (the "omitted" value), so <c>new Version(1, 2)</c> becomes [1, 2] rather than
        /// [1, 2, -1, -1]. No suffix is involved; System.Version has none.
        /// </remarks>
        public QVersionNumber(Version version) : this(ToSegments(version))
        {
        }

        /// <summary>
        /// The version as a BCL <see cref="Version"/>, with omitted trailing segments left at -1.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The QVersionNumber is null/empty or has more than four segments (Version holds at most
        /// major, minor, build and revision), or has a negative segment.
        /// </exception>
        /// <remarks>
        /// A one-segment QVersionNumber gains a zero minor (<c>Version(major, 0)</c>), because
        /// System.Version always has two components. A 3-segment value comes back with
        /// <see cref="Version.Revision"/> == -1; a 2-segment value with both Build and Revision
        /// == -1. The conversion carries no suffix, which QVersionNumber does not store.
        /// </remarks>
        public Version ToVersion()
        {
            int[] segments = Segments() ?? Array.Empty<int>();
            if (segments.Length == 0)
                throw new InvalidOperationException(
                    "The QVersionNumber is null or empty; System.Version always has at least a "
                    + "major and a minor.");
            if (segments.Length > 4)
                throw new InvalidOperationException(
                    $"The QVersionNumber has {segments.Length} segments; System.Version holds at "
                    + "most four (major, minor, build, revision).");
            for (int i = 0; i < segments.Length; i++)
                if (segments[i] < 0)
                    throw new InvalidOperationException(
                        $"The QVersionNumber segment {i} is negative ({segments[i]}); "
                        + "System.Version components are non-negative.");

            switch (segments.Length)
            {
                case 1: return new Version(segments[0], 0);
                case 2: return new Version(segments[0], segments[1]);
                case 3: return new Version(segments[0], segments[1], segments[2]);
                default: return new Version(segments[0], segments[1], segments[2], segments[3]);
            }
        }

        /// <summary>The non-omitted components of a Version, in version-number order.</summary>
        static int[] ToSegments(Version version)
        {
            if (version is null)
                throw new ArgumentNullException(nameof(version));

            int major = version.Major;
            int minor = version.Minor;
            int build = version.Build;
            int revision = version.Revision;
            if (revision >= 0)
                return new[] { major, minor, build, revision };
            if (build >= 0)
                return new[] { major, minor, build };
            return new[] { major, minor };
        }
    }
}
