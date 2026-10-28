using System;
using System.Collections;

// QBitArray and System.Collections.BitArray are both indexed bit strings, and bit i means the same
// bit on both sides: the conversion copies bit by bit in index order, so the on-the-wire bit order
// of Qt's packed bytes is not exposed or reordered. Explicit constructor and ToBitArray(), no
// operators, like the other BCL interop files.
//
// The one representational gap is the length type: QBitArray::size() is qsizetype (64-bit) and
// BitArray::Length is int. A QBitArray longer than int.MaxValue throws InvalidOperationException
// instead of being truncated. A null QBitArray has size 0 and comes back as an empty BitArray;
// BitArray has no null state, and an empty bit string carries the same bits (none).
namespace QtSharp.QtCore
{
    public partial class QBitArray
    {
        /// <summary>Builds a QBitArray with the same bits as a BCL <see cref="BitArray"/>.</summary>
        /// <param name="bits">The bits to copy, index for index.</param>
        /// <exception cref="ArgumentNullException"><paramref name="bits"/> is null.</exception>
        /// <remarks>
        /// The result has bit <c>i</c> set exactly when <c>bits[i]</c> is true, for every index.
        /// The QBitArray owns an independent copy.
        /// </remarks>
        // The cast to long keeps this on the (qsizetype, bool) constructor: the generated class also
        // has a protected QBitArray(IntPtr) constructor, and an int argument in a constructor
        // initializer binds to it (int converts to IntPtr), which would make the handle equal the
        // length instead of pointing at a Qt array.
        public QBitArray(BitArray bits)
            : this((long)CheckedLength((bits ?? throw new ArgumentNullException(nameof(bits))).Length))
        {
            for (int i = 0; i < bits.Length; i++)
            {
                if (bits[i])
                    SetBit(i);
            }
        }

        /// <summary>
        /// The bits as a BCL <see cref="BitArray"/>, index for index.
        /// </summary>
        /// <returns>A new BitArray; changing it does not change the QBitArray.</returns>
        /// <exception cref="InvalidOperationException">
        /// The QBitArray has more than <see cref="int.MaxValue"/> bits, which
        /// <see cref="BitArray.Length"/> cannot express.
        /// </exception>
        /// <remarks>
        /// A null QBitArray has size 0 and comes back as a zero-length BitArray: BitArray has no
        /// null state, and there are no bits to lose. Bit i of the QBitArray is bit i of the
        /// result; Qt's internal byte packing (and its endianness) does not cross.
        /// </remarks>
        public BitArray ToBitArray()
        {
            int length = CheckedLength(Size());
            var result = new BitArray(length);
            for (int i = 0; i < length; i++)
                result[i] = At(i);
            return result;
        }

        /// <summary>
        /// Narrows a QBitArray length to BitArray's int, rejecting the values it cannot hold.
        /// Internal so the guard can be tested without allocating a multi-gigabyte bit array.
        /// </summary>
        internal static int CheckedLength(long size)
        {
            if (size > int.MaxValue)
                throw new InvalidOperationException(
                    $"The QBitArray has {size} bits; System.Collections.BitArray can hold at most "
                    + $"int.MaxValue ({int.MaxValue}) bits.");
            if (size < 0)
                throw new InvalidOperationException(
                    $"The QBitArray size ({size}) is negative and cannot be a BitArray length.");
            return (int)size;
        }
    }
}
