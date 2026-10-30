using System;
using System.Collections.Generic;
using System.IO;

// The System.IO bridge for Qt's filesystem types. QDir and QFileInfo keep their Qt spelling
// because they are not pure values: a QDir carries filter/sort/nameFilters state and a native
// handle, and a QFileInfo can be bound to an open QFileDevice with no path at all. What crosses is
// only what System.IO can represent losslessly:
//
//     var dir  = new QDir(new DirectoryInfo(path));
//     var info = new QFileInfo(new FileInfo(file));
//     FileSystemInfo back = info.ToFileSystemInfo();
//
// The conversions are explicit (constructors and To* methods, no operators), like the other BCL
// interop files. QDir -> DirectoryInfo is lossless for the path but drops the filter state; that
// is why the Qt object itself stays available. QFileInfo -> FileSystemInfo has to choose between
// FileInfo and DirectoryInfo: IsDir() decides when the path exists, and a path that does not exist
// yet maps to FileInfo, because FileSystemInfo is abstract and nothing can know which one it will
// become. ToFileInfo()/ToDirectoryInfo() are the strict pair for callers that do know - they throw
// InvalidOperationException on a mismatch instead of returning the other type.
namespace QtSharp.QtCore
{
    public partial class QDir
    {
        /// <summary>Builds a QDir for the directory a BCL <see cref="DirectoryInfo"/> names.</summary>
        /// <param name="directory">The directory whose <see cref="FileSystemInfo.FullName"/> is used.</param>
        /// <exception cref="ArgumentNullException"><paramref name="directory"/> is null.</exception>
        /// <remarks>
        /// Only the path crosses. The new QDir keeps the Qt defaults (AllEntries, Name |
        /// IgnoreCase); a BCL DirectoryInfo has no filter or sort state to copy.
        /// </remarks>
        public QDir(DirectoryInfo directory)
            : this((directory ?? throw new ArgumentNullException(nameof(directory))).FullName)
        {
        }

        /// <summary>
        /// The directory as a BCL <see cref="DirectoryInfo"/>. The path is QDir's absolute path;
        /// Qt's filter, sorting and name filters stay on the QDir.
        /// </summary>
        /// <remarks>
        /// A QDir that points at a directory which does not exist still converts: DirectoryInfo is
        /// lazy and reports Exists == false, like QDir::exists(). Qt spells paths with '/', which
        /// DirectoryInfo accepts on Windows as well.
        /// </remarks>
        public DirectoryInfo ToDirectoryInfo() => new DirectoryInfo(AbsolutePath());
    }

    public partial class QFileInfo
    {
        /// <summary>
        /// Builds a QFileInfo for the path a BCL <see cref="FileSystemInfo"/> names. Accepts both
        /// FileInfo and DirectoryInfo; the path is <see cref="FileSystemInfo.FullName"/>.
        /// </summary>
        /// <exception cref="ArgumentNullException"><paramref name="info"/> is null.</exception>
        public QFileInfo(FileSystemInfo info)
            : this((info ?? throw new ArgumentNullException(nameof(info))).FullName)
        {
        }

        /// <summary>
        /// The path as a BCL <see cref="FileSystemInfo"/>: a DirectoryInfo when
        /// <see cref="IsDir"/> is true, otherwise a FileInfo.
        /// </summary>
        /// <remarks>
        /// For a path that does not exist IsDir() is false, so the result is a FileInfo that also
        /// reports Exists == false. FileSystemInfo cannot be constructed directly, and there is no
        /// way to tell what a missing path will become; use ToDirectoryInfo() when the caller
        /// already knows.
        /// </remarks>
        public FileSystemInfo ToFileSystemInfo()
            => IsDir() ? (FileSystemInfo)new DirectoryInfo(AbsoluteFilePath())
                       : new FileInfo(AbsoluteFilePath());

        /// <summary>The path as a BCL <see cref="FileInfo"/>.</summary>
        /// <exception cref="InvalidOperationException">The path is a directory.</exception>
        public FileInfo ToFileInfo()
        {
            if (IsDir())
                throw new InvalidOperationException(
                    $"'{AbsoluteFilePath()}' is a directory; use ToDirectoryInfo().");
            return new FileInfo(AbsoluteFilePath());
        }

        /// <summary>The path as a BCL <see cref="DirectoryInfo"/>.</summary>
        /// <exception cref="InvalidOperationException">The path is not a directory.</exception>
        public DirectoryInfo ToDirectoryInfo()
        {
            if (!IsDir())
                throw new InvalidOperationException(
                    $"'{AbsoluteFilePath()}' is not a directory; use ToFileInfo().");
            return new DirectoryInfo(AbsoluteFilePath());
        }
    }

    /// <summary>
    /// <c>foreach</c> over a <see cref="QDirIterator"/>. A Qt iterator is single-pass and owns a
    /// native object, so it is not exposed as an IEnumerable itself; these extensions drive it and
    /// dispose it (including when the loop breaks early) unless <c>disposeIterator</c> is false.
    /// </summary>
    public static class QDirIteratorExtensions
    {
        /// <summary>
        /// The iterator's remaining file paths, one per <see cref="QDirIterator.Next"/>.
        /// </summary>
        /// <param name="iterator">The iterator to drive.</param>
        /// <param name="disposeIterator">
        /// Whether the sequence disposes the iterator when it ends or is abandoned. The default is
        /// true, because the usual spelling creates the iterator inline.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="iterator"/> is null.</exception>
        public static IEnumerable<string> EnumeratePaths(
            this QDirIterator iterator, bool disposeIterator = true)
        {
            if (iterator is null)
                throw new ArgumentNullException(nameof(iterator));

            try
            {
                while (iterator.HasNext())
                    yield return iterator.Next();
            }
            finally
            {
                if (disposeIterator)
                    iterator.Dispose();
            }
        }

        /// <summary>
        /// The iterator's remaining entries as <see cref="QFileInfo"/> values, one per
        /// <see cref="QDirIterator.NextFileInfo"/>.
        /// </summary>
        /// <param name="iterator">The iterator to drive.</param>
        /// <param name="disposeIterator">
        /// Whether the sequence disposes the iterator when it ends or is abandoned. The default is
        /// true.
        /// </param>
        /// <exception cref="ArgumentNullException"><paramref name="iterator"/> is null.</exception>
        /// <remarks>
        /// Each yielded QFileInfo is a separate owned wrapper (Qt returns a copy by value), so the
        /// consumer disposes the elements as well as letting the sequence dispose the iterator.
        /// </remarks>
        public static IEnumerable<QFileInfo> EnumerateFileInfos(
            this QDirIterator iterator, bool disposeIterator = true)
        {
            if (iterator is null)
                throw new ArgumentNullException(nameof(iterator));

            try
            {
                while (iterator.HasNext())
                    yield return iterator.NextFileInfo();
            }
            finally
            {
                if (disposeIterator)
                    iterator.Dispose();
            }
        }
    }
}
