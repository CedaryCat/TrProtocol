using Microsoft.CodeAnalysis;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

namespace TrProtocol.SerializerGenerator.Internal.Serialization;

internal static class SerializeAsTypeValidator
{
    public static void Validate(TypeSerializerContext context) {
        if (context.RoundState.IsEnumRound) {
            return;
        }

        var attribute = context.FieldMemberSym?.GetAttributes()
            .FirstOrDefault(candidate => candidate.AttributeClass?.Name == "SerializeAsAttribute");
        attribute ??= context.PropMemberSym?.GetAttributes()
            .FirstOrDefault(candidate => candidate.AttributeClass?.Name == "SerializeAsAttribute");
        if (attribute is null) {
            return;
        }

        var targetType = attribute.ConstructorArguments.FirstOrDefault().Value as ITypeSymbol;
        var sourceType = context.MemberTypeSym;
        if (IsPrimitiveNumeric(sourceType) && targetType is not null && IsPrimitiveNumeric(targetType)) {
            return;
        }

        throw new DiagnosticException(
            Diagnostic.Create(
                DiagnosticDescriptors.SerializeAsRequiresNumericTypes,
                context.Member.MemberDeclaration.GetLocation(),
                context.Member.MemberName,
                sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat),
                targetType?.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) ?? "<invalid>"));
    }

    private static bool IsPrimitiveNumeric(ITypeSymbol type) => type.SpecialType is
        SpecialType.System_Byte or
        SpecialType.System_SByte or
        SpecialType.System_Int16 or
        SpecialType.System_UInt16 or
        SpecialType.System_Int32 or
        SpecialType.System_UInt32 or
        SpecialType.System_Int64 or
        SpecialType.System_UInt64 or
        SpecialType.System_Single or
        SpecialType.System_Double or
        SpecialType.System_Decimal;
}
