using System;

// Two C# types can share a simple name (they differ by namespace) and declare identical members
// while deriving from different Qt classes. The native table cache therefore keys on the base as
// well: every id in a built table is an offset from the base's counts, so a table built for one of
// these hosts would misnumber the other's methods and properties.
namespace QtSharp.Tests.CachePlain
{
    public sealed class CollidingHost : QObject
    {
        [QtSignal] public event Action Ping;

        public void Fire() => Ping?.Invoke();
    }
}

namespace QtSharp.Tests.CacheWidget
{
    public sealed class CollidingHost : QWidget
    {
        [QtSignal] public event Action Ping;

        public void Fire() => Ping?.Invoke();
    }
}

namespace QtSharp.Tests
{
    /// <summary>
    /// The meta-object cache identity: base class, class name and the declared member list. The
    /// regression is "the second type reuses the first type's table", so the QObject-derived host
    /// is built first and the QWidget-derived one is the one checked.
    /// </summary>
    [Test("test-metaobjectcache")]
    public static class MetaObjectCacheApi
    {
        public static int Run(TestContext context)
        {
            var plain = new CachePlain.CollidingHost();
            plain.Fire();                       // builds and installs the plain host's table first

            using var widgetBase = new QWidget();
            int widgetMethods = widgetBase.MetaObject().MethodCount();
            int widgetProperties = widgetBase.MetaObject().PropertyCount();

            var widget = new CacheWidget.CollidingHost();
            QMetaObject meta = widget.MetaObject();
            context.Check(meta.MethodOffset() == widgetMethods,
                $"a QWidget-derived host counts its methods after QWidget's ({meta.MethodOffset()} vs {widgetMethods})");
            context.Check(meta.PropertyOffset() == widgetProperties,
                $"and its properties after QWidget's ({meta.PropertyOffset()} vs {widgetProperties})");

            int signal = meta.IndexOfSignal("ping()");
            context.Check(signal >= 0 && signal - meta.MethodOffset() == 0,
                $"the widget host's own signal is its first local method (index {signal}, offset {meta.MethodOffset()})");

            int fired = 0;
            widget.Ping += () => fired++;
            widget.Fire();
            context.Check(fired == 1, $"the widget host's signal still reaches its handlers (fired {fired})");

            return context.ExitCode;
        }
    }
}
