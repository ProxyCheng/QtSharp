using CppSharp;
using CppSharp.AST;
using CppSharp.AST.Extensions;
using CppSharp.Generators;
using CppSharp.Types;
using Type = CppSharp.AST.Type;

[TypeMap("QString", GeneratorKindID = GeneratorKind.CSharp_ID)]
public class QStringTypeMap : TypeMap
{
    public override Type SignatureType(TypePrinterContext ctx)
    {
        if (ctx.Kind == TypePrinterContextKind.Managed)
            return new CILType(typeof(string));
        return new CustomType("global::System.IntPtr");
    }

    public override void MarshalToNative(MarshalContext ctx)
    {
        var param = ctx.Parameter.Name;
        var ptr = $"__qs{ctx.ParameterIndex}";
        ctx.Before.WriteLine($"var {ptr} = global::QtSharp.QStringMarshaller.ToNative({param});");
        ctx.Return.Write(ptr);
        ctx.Cleanup.WriteLine($"global::QtSharp.QStringMarshaller.Free({ptr});");
    }

    public override void MarshalToManaged(MarshalContext ctx)
    {
        var v = ctx.ReturnVarName;
        if (Type != null && !Type.Desugar().IsAddress())
            ctx.Return.Write($"global::QtSharp.QStringMarshaller.ToManaged(new global::System.IntPtr(&{v}))");
        else
            ctx.Return.Write($"global::QtSharp.QStringMarshaller.ToManaged({v})");
    }
}
