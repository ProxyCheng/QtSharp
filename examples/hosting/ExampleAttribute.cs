using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

// The example registry is discovery, not a second list: a class declares the name it is run under and
// the hosting assembly finds it in the executable's own assembly. Every module executable has its
// own registry because it carries only that module's examples.
namespace QtSharp.Examples.Hosting
{
    /// <summary>
    /// The name an example class is run under. The class must be static and expose
    /// <c>public static int Run(ExampleContext example)</c>; <see cref="ExampleDiscovery"/> finds it by
    /// reflection.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class ExampleAttribute : Attribute
    {
        public ExampleAttribute(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("An example needs a name.", nameof(name));
            Name = name;
        }

        public string Name { get; }
    }

    /// <summary>Finds every <see cref="ExampleAttribute"/> class in an assembly, in name order.</summary>
    public static class ExampleDiscovery
    {
        public static IReadOnlyList<(string Name, Func<ExampleContext, int> Run)> Find(Assembly assembly)
        {
            var found = new List<(string Name, Func<ExampleContext, int> Run)>();
            foreach (var type in assembly.GetTypes())
            {
                var attribute = type.GetCustomAttribute<ExampleAttribute>();
                if (attribute == null)
                    continue;
                if (!type.IsClass || !type.IsAbstract || !type.IsSealed)
                    throw new InvalidOperationException(
                        $"[Example(\"{attribute.Name}\")] is on '{type.FullName}', which is not a static class.");

                var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(ExampleContext) }, null);
                if (run == null || run.ReturnType != typeof(int))
                    throw new InvalidOperationException(
                        $"[Example(\"{attribute.Name}\")] needs 'public static int Run(ExampleContext example)' " +
                        $"on '{type.FullName}'.");

                var callback = (Func<ExampleContext, int>)run.CreateDelegate(typeof(Func<ExampleContext, int>));
                found.Add((attribute.Name, callback));
            }

            var duplicate = found.GroupBy(d => d.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException($"The example name '{duplicate.Key}' is registered more than once.");

            return found.OrderBy(d => d.Name, StringComparer.Ordinal).ToList();
        }
    }
}
