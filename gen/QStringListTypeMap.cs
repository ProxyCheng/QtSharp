using CppSharp;
using CppSharp.AST;
using CppSharp.AST.Extensions;
using CppSharp.Generators;
using CppSharp.Types;
using Type = CppSharp.AST.Type;

[TypeMap("QStringList", GeneratorKindID = GeneratorKind.CSharp_ID)]
public class QStringListTypeMap : TypeMap
{
    public override Type SignatureType(TypePrinterContext ctx)
    {
        if (ctx.Kind == TypePrinterContextKind.Managed)
            return new CILType(typeof(string[]));
        return new CustomType("global::System.IntPtr");
    }

    public override string CSharpConstruct()
    {
        return "new global::QtSharp.QtCore.QArrayDataPointer.__Internal()";
    }

    public override void MarshalToNative(MarshalContext ctx)
    {
        var param = ctx.Parameter.Name;
        var ptr = $"__qsl{ctx.ParameterIndex}";
        ctx.Before.WriteLine($"var {ptr} = global::QtSharp.QStringListMarshaller.ToNative({param});");
        ctx.Return.Write(ptr);
        ctx.Cleanup.WriteLine($"global::QtSharp.QStringListMarshaller.Free({ptr});");
    }

    public override void MarshalToManaged(MarshalContext ctx)
    {
        var v = ctx.ReturnVarName;
        if (v.Contains("__IntPtr"))
            ctx.Return.Write($"global::QtSharp.QStringListMarshaller.ToManaged({v})");
        else if (Type != null && !Type.Desugar().IsAddress())
            ctx.Return.Write($"global::QtSharp.QStringListMarshaller.ToManaged(new global::System.IntPtr(&{v}))");
        else
            ctx.Return.Write($"global::QtSharp.QStringListMarshaller.ToManaged({v})");
    }
}
