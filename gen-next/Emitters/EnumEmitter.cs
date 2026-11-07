using System.Linq;

namespace QtSharp.Generator
{
    /// <summary>
    /// Enum declarations. Namespace-level and Qt-namespace enums share one file; enums hoisted
    /// out of an erased class (QChar) get their own, because the owning type is not emitted.
    /// </summary>
    public sealed class EnumEmitter : CSharpSubEmitter
    {
        public EnumEmitter(GeneratorContext context) : base(context)
        {
        }

        public override void Emit(CSharpFileSet files)
        {
            var binding = Context.Binding;
            if (binding.Enums.Any(e => binding.ScopeOf(e).Length == 0 || binding.ScopeOf(e) == "Qt"))
            {
                var enums = files.File(CSharpFileSet.PlainKey(Context.Model.Namespace + ".Enums"));
                binding.WriteEnums(enums, "", 1);
                binding.WriteEnums(enums, "Qt", 1);
            }

            if (binding.Enums.Any(e => binding.HoistedEnumNames.ContainsKey(
                    e.GetProperty("qualified").GetString())))
            {
                var enums = files.File(CSharpFileSet.PlainKey(Context.Model.Namespace + ".ErasedEnums"));
                foreach (var e in binding.Enums)
                {
                    string qualified = e.GetProperty("qualified").GetString();
                    if (binding.HoistedEnumNames.TryGetValue(qualified, out var hoisted))
                        binding.WriteEnum(enums, e, 1, name: hoisted);
                }
            }
        }
    }
}
