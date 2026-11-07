using System.Linq;

namespace QtSharp.Generator
{
    /// <summary>
    /// Registers every QObject wrapper this assembly publishes, so resolving an object's actual
    /// type never depends on which class the program touched first.
    /// </summary>
    public sealed class QObjectRegistrationEmitter : CSharpSubEmitter
    {
        public QObjectRegistrationEmitter(GeneratorContext context) : base(context)
        {
        }

        public override void Emit(CSharpFileSet files)
        {
            var binding = Context.Binding;
            var classes = binding.Classes.Where(c => binding.DerivesFromQObject(binding.Ref(c))).ToList();
            if (classes.Count == 0)
                return;

            var registrations = files.File(Context.Model.Namespace + ".QObjectRegistrations.cs");
            registrations.AppendLine("    internal static class QObjectRegistrations");
            registrations.AppendLine("    {");
            registrations.AppendLine("        [ModuleInitializer]");
            registrations.AppendLine("        internal static void Register()");
            registrations.AppendLine("        {");
            foreach (var cls in classes)
            {
                string name = binding.Name(cls);
                string cpp = binding.Ref(cls);
                string alias = cpp == name ? "null" : binding.Quoted(name);
                registrations.AppendLine(
                    $"            QtSharp.QtCore.QObjectTypeRegistry.Register(typeof({name}), {binding.Quoted(cpp)}, {alias}, {name}.__WrapBorrowed_{name}, {name}.__WrapOwned_{name}, {name}.__WrapTransient_{name});");
            }
            registrations.AppendLine("        }");
            registrations.AppendLine("    }");
            registrations.AppendLine();
        }
    }
}
