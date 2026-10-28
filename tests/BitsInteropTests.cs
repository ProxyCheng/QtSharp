using System;
using System.Collections;

namespace QtSharp.Tests
{
    /// <summary>
    /// QBitArray and System.Collections.BitArray (bit order, both directions, the int length
    /// limit) plus QVersionNumber and System.Version (segment arity and omitted segments).
    /// </summary>
    [Test("test-bitsinterop")]
    public static class BitsInteropTests
    {
        public static int Run(TestContext context)
        {
            // ------------------------------------------------------- QBitArray -> BitArray
            using (var array = new QBitArray(10))
            {
                array.SetBit(0);
                array.SetBit(3);
                array.SetBit(9);
                BitArray bits = array.ToBitArray();
                context.Check(bits.Length == 10,
                    $"QBitArray(10) -> BitArray length ({bits.Length})");
                context.Check(bits[0] && !bits[1] && !bits[2] && bits[3]
                      && !bits[4] && !bits[5] && !bits[6] && !bits[7] && !bits[8] && bits[9],
                    "the bits are copied at the same indexes");
            }

            // A 16-bit pattern that is not a palindrome catches a byte- or bit-order reversal.
            var pattern = new BitArray(new[]
            {
                true, false, false, true, true, false, true, false,
                false, false, true, true, true, true, true, false,
            });
            using (var array = new QBitArray(pattern))
            {
                BitArray roundTripped = array.ToBitArray();
                bool same = roundTripped.Length == pattern.Length;
                for (int i = 0; same && i < pattern.Length; i++)
                    same = roundTripped[i] == pattern[i];
                context.Check(same,
                    "BitArray -> QBitArray -> BitArray keeps every bit at its index");
                context.Check(array.Size() == 16,
                    $"BitArray -> QBitArray size ({array.Size()})");
            }

            // ------------------------------------------------------- empty and null
            using (var empty = new QBitArray(0))
            {
                context.Check(!empty.IsNull() && empty.ToBitArray().Length == 0,
                    "an empty (but not null) QBitArray -> zero-length BitArray");
            }

            using (var nullArray = new QBitArray())
            {
                context.Check(nullArray.IsNull() && nullArray.ToBitArray().Length == 0,
                    "a null QBitArray -> zero-length BitArray (no bits to lose)");
            }

            // ------------------------------------------------------- the int length limit
            try
            {
                int length = QBitArray.CheckedLength((long)int.MaxValue + 1);
                context.Check(false, $"a >int.MaxValue QBitArray should not convert (got {length})");
            }
            catch (InvalidOperationException)
            {
                context.Check(true, "a QBitArray above int.MaxValue bits throws InvalidOperationException");
            }

            // ------------------------------------------------------- QVersionNumber -> Version
            using (var version = new QVersionNumber(1, 2, 3))
            {
                Version converted = version.ToVersion();
                context.Check(converted.Major == 1 && converted.Minor == 2 && converted.Build == 3
                      && converted.Revision == -1,
                    $"QVersionNumber(1,2,3) -> Version 1.2.3, revision omitted ({converted})");
            }

            using (var version = new QVersionNumber(7))
            {
                Version converted = version.ToVersion();
                context.Check(converted.Major == 7 && converted.Minor == 0
                      && converted.Build == -1 && converted.Revision == -1,
                    $"a one-segment QVersionNumber gains a zero minor ({converted})");
            }

            // ------------------------------------------------------- Version -> QVersionNumber
            using (var version = new QVersionNumber(new Version(4, 5, 6, 7)))
            {
                context.Check(version.SegmentCount() == 4
                      && version.SegmentAt(0) == 4 && version.SegmentAt(3) == 7,
                    "Version 4.5.6.7 -> QVersionNumber [4, 5, 6, 7]");
            }

            using (var version = new QVersionNumber(new Version(4, 5)))
            {
                context.Check(version.SegmentCount() == 2,
                    $"Version 4.5 -> QVersionNumber [4, 5], build/revision omitted ({version})");
            }

            using (var version = new QVersionNumber(new Version(4, 5, 0)))
            {
                context.Check(version.SegmentCount() == 3 && version.SegmentAt(2) == 0,
                    "a Build of 0 is a segment, not an omission");
            }

            // ------------------------------------------------------- throwing paths
            using (var empty = new QVersionNumber())
            {
                context.Check(empty.SegmentCount() == 0, "the default QVersionNumber has no segments");
                try
                {
                    Version converted = empty.ToVersion();
                    context.Check(false, $"an empty QVersionNumber should not convert (got {converted})");
                }
                catch (InvalidOperationException)
                {
                    context.Check(true, "an empty QVersionNumber throws InvalidOperationException");
                }
            }

            using (var five = new QVersionNumber(new[] { 1, 2, 3, 4, 5 }))
            {
                try
                {
                    Version converted = five.ToVersion();
                    context.Check(false, $"a five-segment QVersionNumber should not convert (got {converted})");
                }
                catch (InvalidOperationException)
                {
                    context.Check(true, "a five-segment QVersionNumber throws InvalidOperationException");
                }
            }

            try
            {
                using var version = new QVersionNumber((Version)null);
                context.Check(false, "a null System.Version should not convert to QVersionNumber");
            }
            catch (ArgumentNullException)
            {
                context.Check(true, "a null System.Version throws ArgumentNullException");
            }

            return context.ExitCode;
        }
    }
}
