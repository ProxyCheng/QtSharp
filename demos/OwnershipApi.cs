using System;
using System.Collections.Generic;
using System.Linq;

// Ownership on the new pipeline: who is allowed to delete a native object.
//
// Qt moves ownership at runtime - a widget put in a layout belongs to its parent from then on -
// while .NET releases things on the garbage collector's schedule. Two rules keep that apart:
//
//   1. only a wrapper that created the object (or was handed a boxed copy) may release it, and
//   2. even then Qt is asked first: an object with a parent is not ours to delete.
//
// The failure this guards against is the one the legacy pipeline hit with QCommandLinkButton: the
// finalizer deleted a widget Qt already owned, and the double free crashed the process.
public static class ApiOwnershipDemo
{
    public static int Run(QApplication app)
    {
        int failures = 0;
        void Check(bool ok, string what)
        {
            Console.WriteLine($"{(ok ? "[PASS]" : "[FAIL]")} {what}");
            if (!ok)
                failures++;
        }

        // A top-level widget that owns a layout full of buttons: Qt owns the buttons from the
        // moment the layout adopts them.
        var parent = new QPushButton("parent");
        var layout = new QVBoxLayout(parent);
        var kept = new List<QPushButton>();
        for (int i = 0; i < 12; i++)
        {
            var button = new QPushButton($"button {i}");
            layout.AddWidget(button);
            kept.Add(button);
        }
        Check(layout.Count() == 12, $"the layout holds twelve widgets (got {layout.Count()})");
        Check(kept[5].Parent() == parent, "the layout reparented the widget");

        // The regression: before the fix each of these wrappers had a finalizer that deleted the
        // widget even though the layout owned it.
        for (int round = 0; round < 3; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Check(kept[5].text() == "button 5",
            $"a widget Qt owns survives repeated full GCs (got '{kept[5].text()}')");
        Check(layout.Count() == 12, $"the layout still holds its widgets (got {layout.Count()})");

        // Disposing a wrapper whose object Qt owns must not destroy the object: the wrapper only
        // gives up its handle.
        var victim = new QPushButton("victim");
        layout.AddWidget(victim);
        int childrenBefore = parent.Children().Length;
        victim.Dispose();
        victim.Dispose();       // the second call is the documented no-op
        Check(parent.Children().Length == childrenBefore,
            $"disposing a reparented widget leaves it alive (got {parent.Children().Length}, expected {childrenBefore})");
        Check(layout.Count() == 13, $"and still in the layout (got {layout.Count()})");

        // The exact crash the legacy pipeline recorded: a QCommandLinkButton inserted into a
        // layout was deleted by its own finalizer, and Qt freed it a second time.
        var linkParent = new QWidget();
        var linkLayout = new QVBoxLayout(linkParent);
        var links = new List<QCommandLinkButton>();
        for (int i = 0; i < 8; i++)
        {
            var link = new QCommandLinkButton($"link {i}");
            linkLayout.AddWidget(link);
            links.Add(link);
        }
        for (int round = 0; round < 3; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Check(links[3].text() == "link 3" && linkLayout.Count() == 8,
            $"QCommandLinkButton in a layout survives GC (got '{links[3].text()}', {linkLayout.Count()} item(s))");

        // A parentless object is the other half of the rule: it really is ours, so disposing it
        // destroys the native object. The application's list of top-level widgets is what makes
        // that observable.
        int topLevelBefore = QApplication.TopLevelWidgets().Length;
        var loose = new QPushButton("loose");
        loose.SetWindowTitle("loose");
        int topLevelWithLoose = QApplication.TopLevelWidgets().Length;
        Check(topLevelWithLoose == topLevelBefore + 1,
            $"a parentless widget is a top-level widget (got {topLevelWithLoose}, was {topLevelBefore})");
        loose.Dispose();
        Check(QApplication.TopLevelWidgets().Length == topLevelBefore,
            $"disposing a parentless widget really destroys it (got {QApplication.TopLevelWidgets().Length})");

        // A second reference to the same native object is the same managed wrapper, so ownership
        // cannot be claimed twice (and dropping the second reference must not free anything).
        var cached = new QPushButton("cached");
        var again = QPushButton.FromHandle(cached.Handle);
        Check(ReferenceEquals(cached, again), "FromHandle returns the wrapper that already exists");
        again = null;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        Check(cached.text() == "cached", "dropping the second reference leaves the owner in charge");

        // Wrappers around objects Qt owns - every element of the top-level list is one - are not
        // owners either, so collecting them is harmless. The list hands out transient wrappers on
        // every call, so the object is identified by its window title rather than by reference.
        parent.SetWindowTitle("owner-window");
        int topLevelBeforeCollect = QApplication.TopLevelWidgets().Length;
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var titles = new List<string>();
        foreach (var top in QApplication.TopLevelWidgets())
            titles.Add(top.windowTitle());
        Check(titles.Contains("owner-window") && titles.Count == topLevelBeforeCollect,
            $"transient wrappers around Qt-owned widgets survive collection (got {titles.Count} top-level widget(s))");

        // ------------------------------------------------------------------ Qt destroyed it
        // A wrapper whose native object Qt destroyed must fail loudly, not touch freed memory
        // (the contract PySide has, where a deleted C++ object raises RuntimeError).
        bool ThrowsOnUse(Action action)
        {
            try
            {
                action();
                return false;
            }
            catch (ObjectDisposedException)
            {
                return true;
            }
        }

        var destroyer = new QPushButton("destroyer");
        var doomed = new QPushButton("doomed");
        doomed.SetParent(destroyer);
        Check(doomed.Parent() == destroyer, "the child was reparented");
        Check(doomed != null && doomed.IsAlive, "a live child is != null and IsAlive");
        var sameDoomed = QPushButton.FromHandle(doomed.Handle);
        Check(sameDoomed == doomed, "the identity cache keeps two wrappers of one object == equal");
        destroyer.Dispose();            // Qt deletes the child with its parent
        Check(ThrowsOnUse(() => doomed.text()), "a child Qt destroyed throws on use");
        Check(ThrowsOnUse(() => destroyer.text()), "and a disposed wrapper does too");

        // The liveness guard, so the throw above never has to happen: a wrapper whose Qt object is
        // gone reads as null, the way a destroyed UnityEngine.Object does. `== null` is what the
        // operator answers; IsAlive is the same question spelled out (sip.isValid in PyQt).
        Check(doomed == null && !doomed.IsAlive, "the Qt-destroyed child reads as == null");
        Check(destroyer == null && !destroyer.IsAlive, "and the disposed wrapper does too");

        // Items are the harder case: a QGraphicsItem is not a QObject and has no destroyed signal,
        // so the scene that owns it is the only lifetime signal there is. The scene is owned by a
        // widget here, which makes its destruction something the test can trigger.
        var sceneOwner = new QPushButton("scene-owner");
        var scene = new QGraphicsScene(sceneOwner);
        var sceneItem = scene.AddRect(new QRectF(0, 0, 30, 30));
        sceneItem.SetZValue(3);
        Check(sceneItem.Rect().Width() == 30,
            $"an item handed over by the scene is usable (got {sceneItem.Rect().Width()})");

        // The GC must leave a scene-owned item alone.
        for (int round = 0; round < 3; round++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        Check(sceneItem.ZValue() == 3, "a scene-owned item survives repeated full GCs");

        sceneOwner.Dispose();           // -> the scene -> its items
        Check(ThrowsOnUse(() => sceneItem.Rect()), "an item whose scene died throws on use");
        Check(ThrowsOnUse(() => scene.Items()), "and so does the scene it belonged to");
        Check(sceneItem == null && !sceneItem.IsAlive, "the dead item reads as == null too");
        Check(scene == null, "and so does the scene that owned it");

        // ---------------------------------------------------------------- the release path
        // "Does Qt own this object?" is a question about the object, not about the class: the
        // generated destructor asks Qt (parent()) and the wrapper only releases what it created.
        // That is what lets a class whose base is QObject directly - QTimer, QThread, QSharedMemory,
        // QUndoStack - have a Dispose at all. It used to be decided per class, by a rule that
        // answered "no release path" for exactly the classes a program creates and should release.
        var timer = new QTimer();
        bool timerDestroyed = false;
        ApiRuntime.Connect(timer.Handle, "destroyed()", (_, _) => timerDestroyed = true);
        Check(timer.Parent() == null, "a QTimer we create has no parent");
        timer.Dispose();
        Check(timerDestroyed && ThrowsOnUse(() => timer.Interval.ToString()),
            "disposing a QObject we created really deletes it (Qt said destroyed)");

        // The same class with a parent belongs to that parent, and Dispose leaves it there.
        var owner = new QObject();
        var ownedTimer = new QTimer(owner);
        bool ownedDestroyed = false;
        ApiRuntime.Connect(ownedTimer.Handle, "destroyed()", (_, _) => ownedDestroyed = true);
        int childrenBeforeDispose = owner.Children().Length;
        ownedTimer.Dispose();
        Check(!ownedDestroyed && owner.Children().Length == childrenBeforeDispose,
            $"a QTimer with a parent survives Dispose (parent still has {owner.Children().Length} child(ren))");

        // The same rule for a document: Qt only owns it when the editor is its QObject parent,
        // which is why the C++ API takes one (`new QTextDocument(edit)`).
        var editor = new QTextEdit();
        var document = new QTextDocument(editor);
        document.SetPlainText("owned by the editor");
        editor.SetDocument(document);
        document.Dispose();
        Check(editor.ToPlainText() == "owned by the editor",
            $"a document whose parent is the editor survives Dispose (got '{editor.ToPlainText()}')");

        // And the case that has no parent to hide behind is marked in the model instead: the
        // clipboard (and QDrag) adopt a QMimeData without reparenting it, so the binding never
        // releases one - a leak rather than a use-after-free.
        var mime = new QMimeData();
        mime.SetText("payload");
        Check(!mime.GetType().GetInterfaces().Any(i => i.Name == "IDisposable"),
            "QMimeData is deliberately not disposable (the clipboard owns it once it is set)");

        // ------------------------------------------------------------------ lifetime generations
        // A native address is not an identity: the allocator hands a freed block to the next object
        // of the same size. Qt's destroy notification is therefore tagged with the generation it was
        // delivered for, so a *late* one - a deleteLater, a parent's teardown, an item scrolled out
        // of a view - cannot kill whatever occupies the address by the time it arrives. Without this
        // the new object's wrappers were invalidated and `Handle` threw ObjectDisposedException.
        var survivor = new QWidget();
        long generation = ApiRuntime.GenerationOf(survivor.Handle);

        ApiRuntime.ProbeDestroyed(survivor.Handle, generation - 1);
        Check(survivor.IsAlive && survivor.Handle != IntPtr.Zero && generation > 0,
            "a destroy notification for a previous occupant leaves the current one alone");

        ApiRuntime.ProbeDestroyed(survivor.Handle, generation);
        Check(!survivor.IsAlive, "a destroy notification for the current occupant invalidates it");

        // ... and the dead marking must not stick to the address: the next object to land there has
        // to be alive and watched again. Otherwise it would silently point at freed memory the day
        // Qt destroys it, because Watch() would believe the address was already gone.
        var later = new QWidget();
        long laterGeneration = ApiRuntime.GenerationOf(later.Handle);
        Check(later.IsAlive && laterGeneration > generation,
            $"a wrapper created later starts a new generation ({generation} -> {laterGeneration})");

        // The reported shape: allocation churn between an item being handed to its owner and the
        // item being used. The addresses freed by the churn are reused, and none of those lifetimes
        // may be inherited by the item.
        var churnScene = new QGraphicsScene();
        var churn = new List<QWidget>();
        for (int i = 0; i < 64; i++)
            churn.Add(new QWidget());
        var item = churnScene.AddRect(new QRectF(0, 0, 10, 10));
        foreach (var widget in churn)
            widget.Dispose();
        for (int i = 0; i < 64; i++)
            churn[i] = new QWidget();
        item.SetRect(new QRectF(0, 0, 20, 20));
        Check(item.Rect().Width() == 20 && item.IsAlive,
            "an item handed to its owner survives allocation churn (address reuse)");
        foreach (var widget in churn)
            widget.Dispose();

        Console.WriteLine(failures == 0 ? "[SELFTEST OK]" : $"[SELFTEST FAILED] {failures}");
        return failures == 0 ? 0 : 1;
    }
}
