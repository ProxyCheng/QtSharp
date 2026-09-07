using CppSharp;
using CppSharp.AST;
using CppSharp.Generators;
using CppSharp.Types;
using Type = CppSharp.AST.Type;

[TypeMap("QFlags", GeneratorKindID = GeneratorKind.CSharp_ID)]
public class QFlagsTypeMap : TypeMap
{
    public override bool IsValueType => true;

    public override Type SignatureType(TypePrinterContext ctx)
        => new BuiltinType(PrimitiveType.Int);
}
