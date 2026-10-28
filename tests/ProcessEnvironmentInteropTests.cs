using System;
using System.Collections.Generic;

namespace QtSharp.Tests
{
    /// <summary>
    /// QProcessEnvironment and IReadOnlyDictionary: a snapshot in both directions, the caller's
    /// comparer, collision refusal, and the inherits-from-parent state that a dictionary cannot hold.
    /// </summary>
    [Test("test-processenvironmentinterop")]
    public static class ProcessEnvironmentInteropTests
    {
        public static int Run(TestContext context)
        {
            // ------------------------------------------------------- dictionary -> environment
            var source = new Dictionary<string, string>
            {
                ["PATH"] = "/opt/bin",
                ["TEMP"] = "/tmp",
            };
            using (var environment = new QProcessEnvironment(source))
            {
                context.Check(environment.Value("PATH") == "/opt/bin" && environment.Value("TEMP") == "/tmp",
                    "IReadOnlyDictionary -> QProcessEnvironment copies every entry");
                context.Check(!environment.InheritsFromParent(),
                    "a dictionary snapshot does not inherit the parent environment");

                // The Qt object copied the entries: changing the source dictionary is not visible.
                source["PATH"] = "/changed";
                context.Check(environment.Value("PATH") == "/opt/bin",
                    "the QProcessEnvironment holds a snapshot, not a view of the dictionary");

                // --------------------------------------------------- environment -> dictionary
                Dictionary<string, string> snapshot =
                    environment.ToDictionary(StringComparer.OrdinalIgnoreCase);
                context.Check(snapshot["path"] == "/opt/bin" && snapshot["temp"] == "/tmp",
                    "the caller's comparer is used for the returned dictionary");
                context.Check(snapshot is IReadOnlyDictionary<string, string>,
                    "the snapshot is an IReadOnlyDictionary");

                // ... and the other direction is a snapshot too.
                snapshot["PATH"] = "/changed";
                context.Check(environment.Value("PATH") == "/opt/bin",
                    "changing the snapshot does not change the QProcessEnvironment");

                // The default comparer is Ordinal: environment names are case-sensitive by default.
                Dictionary<string, string> ordinal = environment.ToDictionary();
                context.Check(ordinal.ContainsKey("PATH") && !ordinal.ContainsKey("path"),
                    "ToDictionary() defaults to StringComparer.Ordinal");
            }

            // ------------------------------------------------------- FromDictionary
            var values = new Dictionary<string, string> { ["LANG"] = "en_US.UTF-8" };
            using (var environment = QProcessEnvironment.FromDictionary(values))
            {
                context.Check(environment.Value("LANG") == "en_US.UTF-8",
                    "FromDictionary copies the entries");
            }

            // ------------------------------------------------------- a comparer collision refuses
            using (var environment = new QProcessEnvironment())
            {
                environment.Insert("PATH", "first");
                environment.Insert("Path", "second");

                // Qt itself folds environment names on Windows: the second Insert replaces the
                // first, and every spelling reads back the same entry. Elsewhere the two names are
                // distinct, which is the case that lets the caller's comparer collide.
                if (OperatingSystem.IsWindows())
                {
                    context.Check(environment.Keys().Length == 1,
                        $"Qt on Windows folds PATH and Path into one name ({environment.Keys().Length})");
                    context.Check(environment.Value("PATH") == "second"
                          && environment.Value("Path") == "second"
                          && environment.Value("path") == "second",
                        "and every spelling reads the surviving value");
                    Dictionary<string, string> collapsed =
                        environment.ToDictionary(StringComparer.OrdinalIgnoreCase);
                    context.Check(collapsed.Count == 1 && collapsed["path"] == "second",
                        "a case-insensitive snapshot of the folded names has one entry");
                }
                else
                {
                    try
                    {
                        Dictionary<string, string> collapsed =
                            environment.ToDictionary(StringComparer.OrdinalIgnoreCase);
                        context.Check(false,
                            $"two case-distinct names should not collapse into one dictionary ({collapsed.Count})");
                    }
                    catch (InvalidOperationException)
                    {
                        context.Check(true,
                            "a case-insensitive comparer over PATH and Path throws InvalidOperationException");
                    }

                    Dictionary<string, string> ordinal = environment.ToDictionary();
                    context.Check(ordinal.Count == 2 && ordinal["PATH"] == "first" && ordinal["Path"] == "second",
                        "the Ordinal snapshot keeps both entries");
                }
            }

            // ------------------------------------------------------- invalid input
            try
            {
                using var environment = new QProcessEnvironment(
                    new Dictionary<string, string> { ["NULL_VALUE"] = null });
                context.Check(false, "a null environment value should not convert");
            }
            catch (InvalidOperationException)
            {
                context.Check(true, "a null environment value throws InvalidOperationException");
            }

            try
            {
                using var environment = new QProcessEnvironment((IReadOnlyDictionary<string, string>)null);
                context.Check(false, "a null dictionary should not convert to QProcessEnvironment");
            }
            catch (ArgumentNullException)
            {
                context.Check(true, "a null dictionary throws ArgumentNullException");
            }

            return context.ExitCode;
        }
    }
}
