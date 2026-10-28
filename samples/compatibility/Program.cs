using System;
using System.Linq;
using System.Reflection;
using QtSharp.QtCore;
using QtSharp.QtWidgets;

static class Program
{
    static void Check(bool condition, string message)
    {
        if (!condition)
            throw new InvalidOperationException(message);
        Console.WriteLine("[PASS] " + message);
    }

    [STAThread]
    static int Main(string[] args)
    {
        if (args.Length != 1)
        {
            Console.Error.WriteLine("Choose one check: strings, layouts, variants, tree, api-gaps");
            return 2;
        }

        Console.WriteLine("Checking: " + args[0]);
        // Only widget scenarios need GUI initialization. Ownership, QObject variants and
        // reflection can be checked even on a host without a graphical desktop.
        QApplication app = null;
        if (args[0] is "strings" or "tree")
            app = new QApplication(Array.Empty<string>());
        switch (args[0])
        {
            case "strings":
                using (var tree = new QTreeWidget())
                {
                    tree.SetColumnCount(2);
                    tree.SetHeaderLabels(new[] { "Key", "值" });
                    Check(tree.HeaderItem().Text(0) == "Key" && tree.HeaderItem().Text(1) == "值",
                        "#60/#79: multiple string[] header labels round-trip");
                    tree.WindowTitle = "QtSharp 中文 😀";
                    Check(tree.WindowTitle == "QtSharp 中文 😀",
                        "#64: Unicode strings round-trip in an x64 application");
                    Check(Environment.Is64BitProcess, "the check runs as a 64-bit process");
                }
                break;
            case "layouts":
                var outer = new QVBoxLayout();
                var inner = new QHBoxLayout();
                outer.AddLayout(inner);
                outer.Dispose();
                Check(!outer.IsAlive && !inner.IsAlive,
                    "#80: disposing a parent layout destroys both native layouts");
                inner.Dispose();
                outer.Dispose();
                GC.Collect();
                GC.WaitForPendingFinalizers();
                Check(true, "#80: repeated Dispose and finalization complete without a crash");
                break;
            case "variants":
                using (var target = new QTimer())
                using (var storage = new QTimer())
                {
                    // Qt returns false when adding a dynamic property even though it stores
                    // the value, so verify by reading the property rather than its return code.
                    storage.SetProperty("qt-object", target);
                    object result = storage.Property("qt-object");
                    Check(ReferenceEquals(result, target) && result is QTimer,
                        "#81: QObject property data returns the original managed wrapper");
                    var payload = new object();
                    storage.SetProperty("managed-object", payload);
                    Check(ReferenceEquals(storage.Property("managed-object"), payload),
                        "#81: arbitrary managed property data preserves reference identity");
                }
                break;
            case "tree":
                using (var tree = new QTreeWidget())
                {
                    tree.SetColumnCount(1);
                    var item = new QTreeWidgetItem();
                    item.SetText(0, "Main");
                    tree.AddTopLevelItem(item);
                    int calls = 0;
                    Action<QTreeWidgetItem, int> handler = (clicked, column) =>
                    {
                        Check(ReferenceEquals(clicked, item) && column == 0 && clicked.Text(0) == "Main",
                            "#57: ItemClicked marshals the existing item and column");
                        calls++;
                    };
                    tree.ItemClicked += handler;
                    tree.ItemClickedSignal.Emit(item, 0);
                    Check(calls == 1, "#57: the native signal reaches the managed event");
                    tree.ItemClicked -= handler;
                    tree.ItemClickedSignal.Emit(item, 0);
                    Check(calls == 1, "#57: removing the event handler disconnects it");
                }
                break;
            case "api-gaps":
                var names = typeof(QObject).GetMembers(BindingFlags.Instance | BindingFlags.Static
                    | BindingFlags.Public | BindingFlags.NonPublic).Select(member => member.Name).ToArray();
                foreach (var name in new[] { "Sender", "sender", "FindChild", "FindChildren" })
                    Console.WriteLine($"[API] QObject.{name}: {(names.Contains(name) ? "present" : "absent")}");
                break;
            default:
                Console.Error.WriteLine("Unknown check: " + args[0]);
                return 2;
        }

        GC.KeepAlive(app);
        return 0;
    }
}
