using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// The demo registry is discovery, not a second list: a class declares the name it is run under and
// the hosting assembly finds it in the executable's own assembly. Every module executable has its
// own registry because it carries only that module's examples.
namespace QtSharp.Demos.Hosting
{
    /// <summary>
    /// The name a demo class is run under. The class must be static and expose
    /// <c>public static int Run(DemoContext demo)</c>; <see cref="DemoDiscovery"/> finds it by
    /// reflection.
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
        public static IReadOnlyList<(string Name, Func<DemoContext, int> Run)> Find(Assembly assembly)
        {
            var found = new List<(string Name, Func<DemoContext, int> Run)>();
            foreach (var type in assembly.GetTypes())
            {
                var attribute = type.GetCustomAttribute<DemoAttribute>();
                if (attribute == null)
                    continue;
                if (!type.IsClass || !type.IsAbstract || !type.IsSealed)
                    throw new InvalidOperationException(
                        $"[Demo(\"{attribute.Name}\")] is on '{type.FullName}', which is not a static class.");

                var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(DemoContext) }, null);
                if (run == null || run.ReturnType != typeof(int))
                    throw new InvalidOperationException(
                        $"[Demo(\"{attribute.Name}\")] needs 'public static int Run(DemoContext demo)' " +
                        $"on '{type.FullName}'.");

                var callback = (Func<DemoContext, int>)run.CreateDelegate(typeof(Func<DemoContext, int>));
                found.Add((attribute.Name, callback));
            }

            var duplicate = found.GroupBy(d => d.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException($"The demo name '{duplicate.Key}' is registered more than once.");

            return found.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        }
    }
}
