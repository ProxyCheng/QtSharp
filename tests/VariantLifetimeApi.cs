using System;

namespace QtSharp.Tests
{
    /// <summary>
    /// The lifetime half of the QVariant bridge. Managed payloads are not values Qt can copy by
    /// itself, so the binding keeps them in a table keyed by a ref-counted native id. Every check
    /// here asserts that the native box and the managed table entry both return to their baseline
    /// once the last QVariant copy is gone - the failure mode would be a silently growing table
    /// (or a freed object still reachable through a Qt copy).
    /// </summary>
    [Test("test-variantlifetime")]
    public static class VariantLifetimeTests
    {
        sealed class Payload
        {
        }

        public static int Run(TestContext context)
        {

            int storeBaseline = ManagedVariantStore.Count;
            long boxBaseline = ManagedVariantStore.NativeBoxCount;
            context.Check(storeBaseline == 0 && boxBaseline == 0,
                $"the table starts empty (store {storeBaseline}, boxes {boxBaseline})");

            // ---------------------------------------------------------------- create/dispose
            for (int i = 0; i < 200; i++)
                using (var variant = new QVariant(new Payload()))
                {
                }
            context.Check(ManagedVariantStore.Count == storeBaseline,
                $"200 disposed variants leave no managed table entries (got {ManagedVariantStore.Count})");
            context.Check(ManagedVariantStore.NativeBoxCount == boxBaseline,
                $"and no native boxes (got {ManagedVariantStore.NativeBoxCount})");

            // ---------------------------------------------------------------- copies share one box
            var payload = new Payload();
            using (var original = new QVariant(payload))
            {
                context.Check(ManagedVariantStore.Count == storeBaseline + 1
                      && ManagedVariantStore.NativeBoxCount == boxBaseline + 1,
                    "one wrapped object has one table entry and one native box");
                using (var clone = new QVariant((object)original))
                {
                    context.Check(ManagedVariantStore.Count == storeBaseline + 1
                          && ManagedVariantStore.NativeBoxCount == boxBaseline + 1,
                        "a QVariant copy shares the payload instead of allocating another box");
                    context.Check(ReferenceEquals(clone.ToObject(), payload), "and unwraps to the same object");
                }
                context.Check(ManagedVariantStore.Count == storeBaseline + 1,
                    "disposing the copy leaves the original's payload alive");
            }
            context.Check(ManagedVariantStore.Count == storeBaseline
                  && ManagedVariantStore.NativeBoxCount == boxBaseline,
                "disposing the original releases the last payload");

            // ---------------------------------------------------------------- Qt-owned copies
            using (var combo = new QComboBox())
            using (var original = new QVariant(payload))
            {
                combo.AddItem("payload", original);
                original.Dispose();
                context.Check(ManagedVariantStore.Count == storeBaseline + 1
                      && ManagedVariantStore.NativeBoxCount == boxBaseline + 1,
                    "Qt's item-data copy keeps exactly one payload alive after the original is gone");
                {
                    context.Check(ReferenceEquals(combo.ItemData(0, (int)Qt.ItemDataRole.UserRole), payload),
                        "the payload is still readable through Qt's copy");
                    context.Check(ManagedVariantStore.Count == storeBaseline + 1,
                        "reading through Qt does not root another payload");
                }
            }
            context.Check(ManagedVariantStore.Count == storeBaseline
                  && ManagedVariantStore.NativeBoxCount == boxBaseline,
                "destroying the combo releases the last payload");

            // ---------------------------------------------------------------- Qt value types
            // QColor is a 16-byte struct: its variant holds Qt's value, and unwrapping hands the
            // value back with no wrapper (and so nothing in the identity cache).
            using (var variant = new QVariant(new QColor(1, 2, 3)))
            {
                context.Check(variant.ToObject() is QColor back && back.IsValid() && back.Red() == 1,
                    "a QColor variant unwraps to the struct");
            }
            context.Check(ManagedVariantStore.Count == storeBaseline
                  && ManagedVariantStore.NativeBoxCount == boxBaseline,
                "QColor conversions use no opaque managed payloads");

            // ---------------------------------------------------------------- Uuid (Guid)
            // QUuid is erased to Guid, so a Uuid variant holds Qt's own 16-byte value: it must not
            // create a managed table entry or a native box (those exist only for opaque payloads).
            // The leak failure mode would be one box per UUID, which nothing would ever release.
            var uuid = new Guid("00112233-4455-6677-8899-aabbccddeeff");
            for (int i = 0; i < 200; i++)
                using (var variant = new QVariant(uuid))
                {
                    if (variant.type() != (int)QVariant.Type.Uuid)
                    {
                        context.Check(false, $"round {i} produced {variant.type()} instead of Uuid");
                        break;
                    }
                }
            context.Check(ManagedVariantStore.Count == storeBaseline,
                $"200 Guid variants leave no managed table entries (got {ManagedVariantStore.Count})");
            context.Check(ManagedVariantStore.NativeBoxCount == boxBaseline,
                $"and no native boxes (got {ManagedVariantStore.NativeBoxCount})");

            // Qt's own copy (an item's data) still unwraps after ours is gone.
            using (var combo = new QComboBox())
            {
                using (var variant = new QVariant(uuid))
                    combo.AddItem("uuid", variant);
                context.Check(combo.ItemData(0, (int)Qt.ItemDataRole.UserRole) is Guid stored && stored == uuid,
                        "Qt's copy of a Uuid variant still unwraps to the Guid");
                context.Check(ManagedVariantStore.Count == storeBaseline,
                    "reading it roots no managed payload");
            }

            // A struct has no wrapper to leave in the identity cache; only the QVariant does.
            using (var variant = new QVariant(new QColor(4, 5, 6)))
                context.Check(variant.ToObject() is QColor stored && stored.Blue() == 6,
                    "a QColor variant round-trips without a native wrapper");

            IntPtr variantHandle;
            using (var variant = new QVariant(new Payload()))
                variantHandle = variant.Handle;
            context.Check(variantHandle != IntPtr.Zero && ApiRuntime.TryGetWrapper(variantHandle) == null,
                "a disposed QVariant leaves the runtime's identity cache");

            // The generated wrappers do not use QVariant at all: they lease the native variant to
            // the ABI. The lease is a reference type precisely so a copy cannot double-free, and
            // the detached wrapper must leave the identity cache just as Dispose does.
            var lease = VariantMarshalling.Value(new Payload());
            IntPtr leaseHandle = lease.Handle;
            var leaseAlias = lease;
            lease.Dispose();
            context.Check(leaseHandle != IntPtr.Zero && lease.Handle == IntPtr.Zero && leaseAlias.Handle == IntPtr.Zero,
                "a VariantLease disposes once for every copy (it is the same object)");
            leaseAlias.Dispose();
            context.Check(ApiRuntime.TryGetWrapper(leaseHandle) == null,
                "and the detached QVariant leaves the runtime's identity cache");
            context.Check(ManagedVariantStore.Count == storeBaseline,
                "the lease's managed payload is released when it is disposed");

            using (var invalid = new QVariant())
            {
                context.Check(invalid.ToObject() == null, "an invalid QVariant unwraps to null");
                context.Check(ManagedVariantStore.Count == storeBaseline
                      && ManagedVariantStore.NativeBoxCount == boxBaseline,
                    "and carries no payload");
            }

            return context.ExitCode;
        }
    }
}
