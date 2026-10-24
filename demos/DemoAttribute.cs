using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using QtSharp.QtWidgets;

// The demo registry is discovery, not a second list: a class declares the name it is run under and
// the catalogs find it in their own assembly. This file is compiled into QtSharp.Demos (the base
// demos and the binding tests link it through the project reference) and source-linked into the
// Chart and Qt3D demo projects, which do not reference the base demo assembly.
namespace QtSharp.Demos
{
    /// <summary>
    /// The name a demo class is run under. The class must be static and expose
    /// <c>public static int Run(QApplication app)</c>; the catalog discovers it by reflection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class DemoAttribute : Attribute
    {
        public DemoAttribute(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A demo needs a name.", nameof(name));
            Name = name;
        }

        public string Name { get; }
    }

    /// <summary>Finds every <see cref="DemoAttribute"/> class in an assembly, in name order.</summary>
    public static class DemoDiscovery
    {
        public static IReadOnlyList<(string Name, Func<QApplication, int> Run)> Find(Assembly assembly)
        {
            var found = new List<(string Name, Func<QApplication, int> Run)>();
            foreach (var type in assembly.GetTypes())
            {
                var attribute = type.GetCustomAttribute<DemoAttribute>();
                if (attribute == null)
                    continue;
                if (!type.IsClass || !type.IsAbstract || !type.IsSealed)
                    throw new InvalidOperationException(
                        $"[Demo(\"{attribute.Name}\")] is on '{type.FullName}', which is not a static class.");

                var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(QApplication) }, null);
                if (run == null || run.ReturnType != typeof(int))
                    throw new InvalidOperationException(
                        $"[Demo(\"{attribute.Name}\")] needs 'public static int Run(QApplication app)' " +
                        $"on '{type.FullName}'.");

                var callback = (Func<QApplication, int>)run.CreateDelegate(typeof(Func<QApplication, int>));
                found.Add((attribute.Name, callback));
            }

            var duplicate = found.GroupBy(d => d.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException($"The demo name '{duplicate.Key}' is registered more than once.");

            return found.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        }
    }
}
