using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace QtSharp.Tests
{
    /// <summary>
    /// The name a binding test runs under. Tests are our own checks of the binding, not ports of
    /// official examples; the class must be static and expose
    /// <c>public static int Run(TestContext context)</c>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
    public sealed class TestAttribute : Attribute
    {
        public TestAttribute(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("A test needs a name.", nameof(name));
            Name = name;
        }

        public string Name { get; }
    }

    /// <summary>Finds every <see cref="TestAttribute"/> class in an assembly, in name order.</summary>
    public static class TestDiscovery
    {
        public static IReadOnlyList<(string Name, Func<TestContext, int> Run)> Find(Assembly assembly)
        {
            var found = new List<(string Name, Func<TestContext, int> Run)>();
            foreach (var type in assembly.GetTypes())
            {
                var attribute = type.GetCustomAttribute<TestAttribute>();
                if (attribute == null)
                    continue;
                if (!type.IsClass || !type.IsAbstract || !type.IsSealed)
                    throw new InvalidOperationException(
                        $"[Test(\"{attribute.Name}\")] is on '{type.FullName}', which is not a static class.");

                var run = type.GetMethod("Run", BindingFlags.Public | BindingFlags.Static,
                    null, new[] { typeof(TestContext) }, null);
                if (run == null || run.ReturnType != typeof(int))
                    throw new InvalidOperationException(
                        $"[Test(\"{attribute.Name}\")] needs 'public static int Run(TestContext context)' " +
                        $"on '{type.FullName}'.");

                var callback = (Func<TestContext, int>)run.CreateDelegate(typeof(Func<TestContext, int>));
                found.Add((attribute.Name, callback));
            }

            var duplicate = found.GroupBy(t => t.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicate != null)
                throw new InvalidOperationException($"The test name '{duplicate.Key}' is registered more than once.");

            return found.OrderBy(t => t.Name, StringComparer.Ordinal).ToList();
        }
    }
}
