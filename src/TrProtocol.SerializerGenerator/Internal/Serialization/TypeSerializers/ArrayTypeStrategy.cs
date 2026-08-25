using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrProtocol.Attributes;
using TrProtocol.Exceptions;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator;
using TrProtocol.SerializerGenerator.Internal.Conditions.Analysis;
using TrProtocol.SerializerGenerator.Internal.Generation;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.ReadPlan;
using TrProtocol.SerializerGenerator.Internal.Serialization;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;

/// <summary>
/// Array-type serialization strategy.
/// Handles regular arrays, 7-bit-encoded arrays, sparse arrays, and repeat-element arrays.
/// </summary>
public class ArrayTypeStrategy : ITypeSerializerStrategy
{
    public bool StopPropagation => true;

    private readonly CompilationContext _compilationContext;

    private enum ArrayLengthValidationKind
    {
        Unknown = 0,
        NonNegativeWithinInt = 1,
        CheckLowerBoundOnly = 2,
        CheckUpperBoundOnly = 3,
        CheckLowerAndUpperBound = 4,
    }

    public ArrayTypeStrategy(CompilationContext compilationContext) {
        _compilationContext = compilationContext;
    }
    public bool CanHandle(TypeSerializerContext context) {
        return context.Member.MemberType is ArrayTypeSyntax && !context.RoundState.IsArrayRound;
    }

    public void GenerateSerialization(TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var memberTypeSym = context.MemberTypeSym;
        var parentVar = context.ParentVar;

        var arr = (ArrayTypeSyntax)m.MemberType;
        var eleSym = ((IArrayTypeSymbol)memberTypeSym).ElementType;

        // Validate unsupported multi-dimensional arrays.
        if (arr.RankSpecifiers.Count != 1 || context.RoundState.IsArrayRound) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArrayElementShouldNotBeArray,
                    m.MemberDeclaration.GetLocation(),
                    context.Model.TypeName,
                    m.MemberName));
        }

        var has7BitEncoded = Int7BitEncodedStrategy.Is7BitEncoded(context);
        var sparseAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<SparseArrayAttribute>());
        var terminatedAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<TerminatedArrayAttribute>());
        var lengthPrefixedAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<LengthPrefixedArrayAttribute>());
        var hasArraySizeAtt = m.Attributes.Any(a => a.AttributeMatch<ArraySizeAttribute>());

        if (lengthPrefixedAtt is not null && hasArraySizeAtt) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.LengthPrefixedArrayConflictsWithArraySize,
                    lengthPrefixedAtt.GetLocation(),
                    m.MemberName));
        }
        if (lengthPrefixedAtt is not null && has7BitEncoded) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.LengthPrefixedArrayUnsupportedCombination,
                    lengthPrefixedAtt.GetLocation(),
                    m.MemberName,
                    nameof(Int7BitEncodedAttribute)));
        }
        if (lengthPrefixedAtt is not null && sparseAtt is not null) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.LengthPrefixedArrayUnsupportedCombination,
                    lengthPrefixedAtt.GetLocation(),
                    m.MemberName,
                    nameof(SparseArrayAttribute)));
        }
        if (lengthPrefixedAtt is not null && terminatedAtt is not null) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.LengthPrefixedArrayUnsupportedCombination,
                    lengthPrefixedAtt.GetLocation(),
                    m.MemberName,
                    nameof(TerminatedArrayAttribute)));
        }

        // Check for 7-bit encoded arrays.
        if (has7BitEncoded) {
            Generate7BitEncodedArray(context, seriBlock, deserBlock, arr, eleSym);
            return;
        }

        // Check for sparse arrays.
        if (sparseAtt is not null) {
            GenerateSparseArray(context, seriBlock, deserBlock, sparseAtt, eleSym);
            return;
        }

        // Check for sparse arrays.
        if (terminatedAtt is not null) {
            GenerateTerminatedArray(context, seriBlock, deserBlock, terminatedAtt, eleSym);
            return;
        }

        // Regular array handling.
        GenerateRegularArray(context, seriBlock, deserBlock, arr, eleSym, lengthPrefixedAtt);
    }

    private void Generate7BitEncodedArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        ArrayTypeSyntax arr, ITypeSymbol eleSym) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;

        var eleTypeStr = eleSym.GetPredifinedName();
        bool isIntArray = eleTypeStr == "int" || eleTypeStr == nameof(Int32);
        bool isIntEnumArray = eleSym.TypeKind == TypeKind.Enum
            && ((INamedTypeSymbol)eleSym).EnumUnderlyingType?.SpecialType == SpecialType.System_Int32;

        if (!isIntArray && !isIntEnumArray) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.Int7BitEncodedMemberInvalidType,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    eleTypeStr));
        }

        var arrAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<ArraySizeAttribute>())
            ?? throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArraySizeMissing,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName));

        var indexExps = arrAtt.ExtractAttributeParams();
        if (indexExps.Length != 1 || arr.RankSpecifiers[0].Rank != 1) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.Int7BitEncodedArrayMultiDim,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    arr.RankSpecifiers[0].Rank));
        }

        var arrSize = ExtractSizeExpression(indexExps[0], parentVar);
        var indexId = context.NextIndexId();

        // Serialization
        seriBlock.Write($"for (int _i7b_{indexId} = 0; _i7b_{indexId} < {memberAccess}.Length; _i7b_{indexId}++) ");
        seriBlock.BlockWrite((source) => {
            if (isIntEnumArray)
                source.WriteLine($"CommonCode.Write7BitEncodedInt(ref ptr_current, (int){memberAccess}[_i7b_{indexId}]);");
            else
                source.WriteLine($"CommonCode.Write7BitEncodedInt(ref ptr_current, {memberAccess}[_i7b_{indexId}]);");
        });
        seriBlock.WriteLine();

        // Deserialization
        var countName = EmitValidatedArrayLength(
            deserBlock,
            arrSize,
            $"_g_count_{indexId}",
            context.Model.TypeName,
            m.MemberName,
            InferValidationKindForSizeExpression(indexExps[0], context.ModelSym));
        deserBlock.WriteLine($"{memberAccess} = new {eleSym.Name}[{countName}];");
        deserBlock.Write($"for (int _i7b_{indexId} = 0; _i7b_{indexId} < {countName}; _i7b_{indexId}++) ");
        deserBlock.BlockWrite((source) => {
            if (isIntEnumArray)
                GenerationHelpers.WriteDebugRelease(
                    source,
                    $"{memberAccess}[_i7b_{indexId}] = ({eleSym.Name})CommonCode.Read7BitEncodedInt(ref ptr_current, ptr_end, nameof({context.Model.TypeName}), \"{m.MemberName}\", _i7b_{indexId});",
                    $"{memberAccess}[_i7b_{indexId}] = ({eleSym.Name})CommonCode.Read7BitEncodedInt(ref ptr_current, ptr_end);");
            else
                GenerationHelpers.WriteDebugRelease(
                    source,
                    $"{memberAccess}[_i7b_{indexId}] = CommonCode.Read7BitEncodedInt(ref ptr_current, ptr_end, nameof({context.Model.TypeName}), \"{m.MemberName}\", _i7b_{indexId});",
                    $"{memberAccess}[_i7b_{indexId}] = CommonCode.Read7BitEncodedInt(ref ptr_current, ptr_end);");
        });
        deserBlock.WriteLine();
    }

    private static HashSet<string> supportedPrimitiveTypes = [
        "byte", "sbyte", "short", "ushort", "int", "uint", "long", "ulong", "float", "double",
        "Byte", "SByte", "Int16", "UInt16", "Int32", "UInt32", "Int64", "UInt64", "Single", "Double"
    ];

    private void GenerateSparseArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        AttributeSyntax sparseAtt, ITypeSymbol eleSym) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;

        var sparseParams = sparseAtt.ExtractAttributeParams();
        if (sparseParams.Length < 1) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.SparseArrayMissingSize,
                    sparseAtt.GetLocation(),
                    m.MemberName));
        }

        var eleTypeStr = eleSym.GetPredifinedName();
        if (!supportedPrimitiveTypes.Contains(eleTypeStr)) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.SparseArrayInvalidType,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    eleTypeStr));
        }

        var sparseSize = ExtractSizeExpression(sparseParams[0], parentVar);
        var sparseTerminator = sparseParams.Length >= 2 && sparseParams[1] is not null
            ? ExtractSizeExpression(sparseParams[1], parentVar)
            : "ushort.MaxValue";

        var indexId = context.NextIndexId();

        // Serialization
        seriBlock.Write($"for (ushort _sparse_i_{indexId} = 0; _sparse_i_{indexId} < {memberAccess}.Length; _sparse_i_{indexId}++) ");
        seriBlock.BlockWrite((source) => {
            source.Write($"if ({memberAccess}[_sparse_i_{indexId}] != default) ");
            source.BlockWrite((inner) => {
                inner.WriteLine($"Unsafe.Write(ptr_current, _sparse_i_{indexId});");
                inner.WriteLine($"ptr_current = Unsafe.Add<ushort>(ptr_current, 1);");
                inner.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}[_sparse_i_{indexId}]);");
                inner.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");
            });
        });
        seriBlock.WriteLine($"Unsafe.Write(ptr_current, (ushort){sparseTerminator});");
        seriBlock.WriteLine($"ptr_current = Unsafe.Add<ushort>(ptr_current, 1);");
        seriBlock.WriteLine();

        // Deserialization
        var sparseSizeName = EmitValidatedArrayLength(
            deserBlock,
            sparseSize,
            $"_g_sparse_size_{indexId}",
            context.Model.TypeName,
            m.MemberName,
            InferValidationKindForSizeExpression(sparseParams[0], context.ModelSym));
        deserBlock.WriteLine($"{memberAccess} = new {eleTypeStr}[{sparseSizeName}];");
        deserBlock.WriteLine($"var _sparse_loop_{indexId} = 0;");
        deserBlock.Write($"while (true) ");
        deserBlock.BlockWrite((source) => {
            EmitLoopSegmentCheck(
                source,
                context.Model.TypeName,
                m.MemberName,
                "sizeof(ushort)",
                $"_sparse_loop_{indexId}");
            source.WriteLine($"var _sparse_idx_{indexId} = Unsafe.Read<ushort>(ptr_current);");
            source.WriteLine($"ptr_current = Unsafe.Add<ushort>(ptr_current, 1);");
            source.Write($"if (_sparse_idx_{indexId} == {sparseTerminator}) ");
            source.BlockWrite((inner) => {
                inner.WriteLine("break;");
            });
            source.WriteLine($"if (_sparse_idx_{indexId} >= {memberAccess}.Length) throw new ProtocolArrayIndexException(nameof({context.Model.TypeName}), \"{m.MemberName}\", (long)ptr_current, (long)ptr_end, sizeof({eleTypeStr}), _sparse_idx_{indexId}, {memberAccess}.Length, _sparse_loop_{indexId});");
            EmitLoopSegmentCheck(
                source,
                context.Model.TypeName,
                m.MemberName,
                $"sizeof({eleTypeStr})",
                $"_sparse_loop_{indexId}");
            source.WriteLine($"{memberAccess}[_sparse_idx_{indexId}] = Unsafe.Read<{eleTypeStr}>(ptr_current);");
            source.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");
            source.WriteLine($"++_sparse_loop_{indexId};");
        });
        deserBlock.WriteLine();
    }
    private void GenerateTerminatedArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        AttributeSyntax terminatedAtt, ITypeSymbol eleSym) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;

        var terminatedParams = terminatedAtt.ExtractAttributeParams();
        if (terminatedParams.Length < 1) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayMissingMaxCount,
                    terminatedAtt.GetLocation(),
                    m.MemberName));
        }

        var maxSize = ExtractSizeExpression(terminatedParams[0], parentVar);
        var terminatorExpr = terminatedParams.Length >= 2 && terminatedParams[1] is not null
            ? ExtractSizeExpression(terminatedParams[1], parentVar)
            : null;

        var indexId = context.NextIndexId();
        var maxSizeName = EmitValidatedArrayLength(
            deserBlock,
            maxSize,
            $"_g_max_size_{indexId}",
            context.Model.TypeName,
            m.MemberName,
            InferValidationKindForSizeExpression(terminatedParams[0], context.ModelSym));


        var eleTypeStr = eleSym.GetPredifinedName();
        if (supportedPrimitiveTypes.Contains(eleTypeStr)) {
            var terminator = terminatorExpr ?? "default";
            seriBlock.Write($"for (int _sparse_i_{indexId} = 0; _sparse_i_{indexId} < {memberAccess}.Length; _sparse_i_{indexId}++) ");
            seriBlock.BlockWrite(source => {
                source.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}[_sparse_i_{indexId}]);");
                source.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");
            });
            seriBlock.WriteLine($"Unsafe.Write(ptr_current, ({eleTypeStr}){terminator});");
            seriBlock.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");

            // Small, non-escaping `new` array with known length can be stack-allocated and bounds-checks elided; no need to rent from ArrayPool.
            deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new {eleSym.Name}[{maxSizeName}];"); 
            deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
            deserBlock.Write($"for (int _sparse_i_{indexId} = 0; _sparse_i_{indexId} < {maxSizeName}; _sparse_i_{indexId}++) ");
            deserBlock.BlockWrite(source => {
                EmitLoopSegmentCheck(
                    source,
                    context.Model.TypeName,
                    m.MemberName,
                    $"sizeof({eleTypeStr})",
                    $"_sparse_i_{indexId}");
                source.WriteLine($"var _term_value_{indexId} = Unsafe.Read<{eleTypeStr}>(ptr_current);");
                source.WriteLine($"ptr_current = Unsafe.Add<{eleTypeStr}>(ptr_current, 1);");
                source.WriteLine($"if (_term_value_{indexId} == {terminator}) break;");
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = _term_value_{indexId};");
                source.WriteLine($"++_g_arrayIndex_{indexId};");
            });
            deserBlock.WriteLine($"{memberAccess} = new {eleSym.Name}[_g_arrayIndex_{indexId}];");
            deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
            deserBlock.WriteLine();
            return;
        }

        // bool[] is encoded as byte (see BooleanTypeStrategy).
        if (eleTypeStr is "bool" or nameof(Boolean)) {
            var terminator = terminatorExpr ?? "default";
            seriBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {memberAccess}.Length; _term_i_{indexId}++) ");
            seriBlock.BlockWrite(source => {
                source.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}[_term_i_{indexId}] ? (byte)1 : (byte)0);");
                source.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
            });
            seriBlock.WriteLine($"Unsafe.Write(ptr_current, ({terminator}) ? (byte)1 : (byte)0);");
            seriBlock.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");

            deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new bool[{maxSizeName}];");
            deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
            deserBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {maxSizeName}; _term_i_{indexId}++) ");
            deserBlock.BlockWrite(source => {
                EmitLoopSegmentCheck(
                    source,
                    context.Model.TypeName,
                    m.MemberName,
                    "1",
                    $"_term_i_{indexId}");
                source.WriteLine("var _term_value_byte = Unsafe.Read<byte>(ptr_current);");
                source.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
                source.WriteLine("var _term_value = _term_value_byte != 0;");
                source.WriteLine($"if (_term_value == {terminator}) break;");
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = _term_value;");
                source.WriteLine($"++_g_arrayIndex_{indexId};");
            });
            deserBlock.WriteLine($"{memberAccess} = new bool[_g_arrayIndex_{indexId}];");
            deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
            deserBlock.WriteLine();
            return;
        }

        if (eleTypeStr is "char" or nameof(Char)) {
            var terminator = terminatorExpr ?? "default";
            seriBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {memberAccess}.Length; _term_i_{indexId}++) ");
            seriBlock.BlockWrite(source => {
                source.WriteLine($"Unsafe.Write(ptr_current, {memberAccess}[_term_i_{indexId}]);");
                source.WriteLine("ptr_current = Unsafe.Add<char>(ptr_current, 1);");
            });
            seriBlock.WriteLine($"Unsafe.Write(ptr_current, (char){terminator});");
            seriBlock.WriteLine("ptr_current = Unsafe.Add<char>(ptr_current, 1);");

            deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new char[{maxSizeName}];");
            deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
            deserBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {maxSizeName}; _term_i_{indexId}++) ");
            deserBlock.BlockWrite(source => {
                EmitLoopSegmentCheck(
                    source,
                    context.Model.TypeName,
                    m.MemberName,
                    "sizeof(char)",
                    $"_term_i_{indexId}");
                source.WriteLine("var _term_value = Unsafe.Read<char>(ptr_current);");
                source.WriteLine("ptr_current = Unsafe.Add<char>(ptr_current, 1);");
                source.WriteLine($"if (_term_value == (char){terminator}) break;");
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = _term_value;");
                source.WriteLine($"++_g_arrayIndex_{indexId};");
            });
            deserBlock.WriteLine($"{memberAccess} = new char[_g_arrayIndex_{indexId}];");
            deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
            deserBlock.WriteLine();
            return;
        }

        // enum[] is written as its underlying type (see EnumTypeStrategy + PrimitiveTypeStrategy).
        if (eleSym is INamedTypeSymbol { EnumUnderlyingType: not null } enumSym) {
            var enumUnderlying = enumSym.EnumUnderlyingType!;
            var underlyingStr = enumUnderlying.GetPredifinedName();
            if (!supportedPrimitiveTypes.Contains(underlyingStr)) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.TerminatedArrayInvalidElementType,
                        m.MemberDeclaration.GetLocation(),
                        m.MemberName,
                        eleTypeStr,
                        eleTypeStr));
            }

            var terminator = terminatorExpr ?? "default";
            seriBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {memberAccess}.Length; _term_i_{indexId}++) ");
            seriBlock.BlockWrite(source => {
                source.WriteLine($"Unsafe.Write(ptr_current, ({underlyingStr}){memberAccess}[_term_i_{indexId}]);");
                source.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");
            });
            seriBlock.WriteLine($"Unsafe.Write(ptr_current, ({underlyingStr})({terminator}));");
            seriBlock.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");

            deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new {enumSym.Name}[{maxSizeName}];");
            deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
            deserBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {maxSizeName}; _term_i_{indexId}++) ");
            deserBlock.BlockWrite(source => {
                EmitLoopSegmentCheck(
                    source,
                    context.Model.TypeName,
                    m.MemberName,
                    $"sizeof({underlyingStr})",
                    $"_term_i_{indexId}");
                source.WriteLine($"var _term_value_raw_{indexId} = Unsafe.Read<{underlyingStr}>(ptr_current);");
                source.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");
                source.WriteLine($"if (_term_value_raw_{indexId} == ({underlyingStr})({terminator})) break;");
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = ({enumSym.Name})_term_value_raw_{indexId};");
                source.WriteLine($"++_g_arrayIndex_{indexId};");
            });
            deserBlock.WriteLine($"{memberAccess} = new {enumSym.Name}[_g_arrayIndex_{indexId}];");
            deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
            deserBlock.WriteLine();
            return;
        }

        // Non-primitive: struct element where the termination key is the first serializable public member.
        if (eleSym is not INamedTypeSymbol eleNamed || !eleNamed.IsValueType) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidElementType,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    "struct",
                    eleTypeStr));
        }

        bool hasCustomSerializer = eleNamed.AllInterfaces.Any(i =>
            i.Name == nameof(IPackedSerializable)
            || i.Name == nameof(IBinarySerializable)
            || i.Name == nameof(ISerializableView<>));
        if (hasCustomSerializer) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayElementHasCustomSerializer,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    eleNamed.Name));
        }

        if (!_compilationContext.TryGetTypeDefSyntax(eleNamed.Name, out var tdef, context.Model.Namespace, context.Model.Imports) || tdef is null) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayElementNotInlineExpandable,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    eleNamed.Name));
        }

        var typeInfo = context.TransformCallback(tdef);
        if (typeInfo.Members.Length == 0) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    "<none>",
                    eleNamed.Name,
                    "<none>"));
        }

        var keyMember = typeInfo.Members[0];
        MemberSymbolResolver.ResolveMemberSymbol(eleNamed, keyMember, out var keyTypeSym, out var keyFieldSym, out var keyPropSym);

        if ((keyFieldSym?.IsStatic ?? false) || (keyPropSym?.IsStatic ?? false)) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                    keyMember.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    keyMember.MemberName,
                    eleNamed.Name,
                    "static"));
        }

        if (keyMember.Attributes.Any(a => a.Name.ToString().Contains("Condition", StringComparison.Ordinal))) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                    keyMember.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    keyMember.MemberName,
                    eleNamed.Name,
                    "conditional"));
        }

        // The termination key cannot be SerializeAs: it would desynchronize the key read/compare from the inline layout.
        var keyMemberAttributes = keyFieldSym?.GetAttributes() ?? keyPropSym?.GetAttributes() ?? [];
        if (keyMemberAttributes.Any(a => a.AttributeClass?.Name == "SerializeAsAttribute")) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayKeyMemberSerializeAsNotSupported,
                    keyMember.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    keyMember.MemberName,
                    eleNamed.Name));
        }

        string keyTerminator = terminatorExpr ?? "default";
        bool keyIsBool = keyTypeSym.Name is nameof(Boolean);
        bool keyIsChar = keyTypeSym.Name is nameof(Char);
        bool keyIsEnum = keyTypeSym is INamedTypeSymbol { EnumUnderlyingType: not null };

        string keyWireTypeStr;
        string keyMemberTypeStr = keyTypeSym.GetPredifinedName();
        string keyCompareExpr;
        Action<BlockNode, string> writeKey;
        Action<BlockNode, string> readKey;

        if (keyIsBool) {
            keyWireTypeStr = "byte";
            keyCompareExpr = keyTerminator;
            writeKey = (block, access) => {
                block.WriteLine($"Unsafe.Write(ptr_current, {access} ? (byte)1 : (byte)0);");
                block.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
            };
            readKey = (block, outVar) => {
                EmitLoopSegmentCheck(
                    block,
                    context.Model.TypeName,
                    m.MemberName,
                    "1",
                    $"_term_i_{indexId}");
                block.WriteLine($"var {outVar}_byte = Unsafe.Read<byte>(ptr_current);");
                block.WriteLine("ptr_current = Unsafe.Add<byte>(ptr_current, 1);");
                block.WriteLine($"var {outVar} = {outVar}_byte != 0;");
            };
        }
        else if (keyIsEnum) {
            var keyEnum = (INamedTypeSymbol)keyTypeSym;
            var underlying = keyEnum.EnumUnderlyingType!;
            var underlyingStr = underlying.GetPredifinedName();
            if (!supportedPrimitiveTypes.Contains(underlyingStr)) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                        keyMember.MemberDeclaration.GetLocation(),
                        m.MemberName,
                        keyMember.MemberName,
                        eleNamed.Name,
                        keyMemberTypeStr));
            }

            keyWireTypeStr = underlyingStr;
            keyCompareExpr = $"({underlyingStr})({keyTerminator})";
            writeKey = (block, access) => {
                block.WriteLine($"Unsafe.Write(ptr_current, ({underlyingStr}){access});");
                block.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");
            };
            readKey = (block, outVar) => {
                EmitLoopSegmentCheck(
                    block,
                    context.Model.TypeName,
                    m.MemberName,
                    $"sizeof({underlyingStr})",
                    $"_term_i_{indexId}");
                block.WriteLine($"var {outVar}_raw = Unsafe.Read<{underlyingStr}>(ptr_current);");
                block.WriteLine($"ptr_current = Unsafe.Add<{underlyingStr}>(ptr_current, 1);");
                block.WriteLine($"var {outVar} = ({keyEnum.Name}){outVar}_raw;");
            };
        }
        else if (keyIsChar || supportedPrimitiveTypes.Contains(keyMemberTypeStr)) {
            keyWireTypeStr = keyMemberTypeStr;
            keyCompareExpr = $"({keyWireTypeStr})({keyTerminator})";
            writeKey = (block, access) => {
                block.WriteLine($"Unsafe.Write(ptr_current, {access});");
                block.WriteLine($"ptr_current = Unsafe.Add<{keyWireTypeStr}>(ptr_current, 1);");
            };
            readKey = (block, outVar) => {
                EmitLoopSegmentCheck(
                    block,
                    context.Model.TypeName,
                    m.MemberName,
                    $"sizeof({keyWireTypeStr})",
                    $"_term_i_{indexId}");
                block.WriteLine($"var {outVar} = Unsafe.Read<{keyWireTypeStr}>(ptr_current);");
                block.WriteLine($"ptr_current = Unsafe.Add<{keyWireTypeStr}>(ptr_current, 1);");
            };
        }
        else {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.TerminatedArrayInvalidTerminationKey,
                    keyMember.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    keyMember.MemberName,
                    eleNamed.Name,
                    keyMemberTypeStr));
        }

        var elementVar = $"_term_ele_{indexId}";
        var keyValueVar = $"_term_key_{indexId}";
        var keyAccess = $"{elementVar}.{keyMember.MemberName}";
        var remainingMembers = typeInfo.Members.Skip(1)
            .Select<SerializationExpandContext, (SerializationExpandContext m, string? parant_var, RoundState roundState)>(m2 => (m2, elementVar, RoundState.Empty));

        // Serialization: write key first, then write remaining members; finally write terminator key only.
        seriBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {memberAccess}.Length; _term_i_{indexId}++) ");
        seriBlock.BlockWrite(source => {
            source.WriteLine($"var {elementVar} = {memberAccess}[_term_i_{indexId}];");
            writeKey(source, keyAccess);

            var dummyDeser = new BlockNode(source);
            context.ExpandMembersCallback(source, dummyDeser, eleNamed, remainingMembers);
        });
        writeKey(seriBlock, $"({keyMemberTypeStr})({keyTerminator})");
        seriBlock.WriteLine();

        // Deserialization
        deserBlock.WriteLine($"var _g_arrayCache_{indexId} = new {eleNamed.Name}[{maxSizeName}];");
        deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
        deserBlock.Write($"for (int _term_i_{indexId} = 0; _term_i_{indexId} < {maxSizeName}; _term_i_{indexId}++) ");
        deserBlock.BlockWrite(source => {
            readKey(source, keyValueVar);
            source.WriteLine($"if ({(keyIsBool ? keyValueVar : (keyIsEnum ? $"{keyValueVar}_raw" : keyValueVar))} == {keyCompareExpr}) break;");

            source.WriteLine($"{eleNamed.Name} {elementVar} = default;");
            source.WriteLine($"{keyAccess} = {keyValueVar};");

            var dummySeri = new BlockNode(source);
            context.ExpandMembersCallback(dummySeri, source, eleNamed, remainingMembers);

            source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = {elementVar};");
            source.WriteLine($"++_g_arrayIndex_{indexId};");
        });
        deserBlock.WriteLine($"{memberAccess} = new {eleNamed.Name}[_g_arrayIndex_{indexId}];");
        deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
        deserBlock.WriteLine();
    }

    private void GenerateRegularArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        ArrayTypeSyntax arr, ITypeSymbol eleSym, AttributeSyntax? lengthPrefixedAtt) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;
        var model = context.Model;

        if (lengthPrefixedAtt is not null) {
            GenerateLengthPrefixedRegularArray(context, seriBlock, deserBlock, arr, eleSym, lengthPrefixedAtt);
            return;
        }

        var arrAtt = m.Attributes.FirstOrDefault(a => a.AttributeMatch<ArraySizeAttribute>())
            ?? throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArraySizeMissing,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    model.TypeName));

        var indexExps = arrAtt.ExtractAttributeParams();
        if (indexExps.Length != arr.RankSpecifiers[0].Rank) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArrayRankConflict,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName,
                    model.TypeName));
        }

        bool elementRepeating = eleSym.AllInterfaces.Any(i => i.MetadataName == "IRepeatElement`1");
        if (elementRepeating && arr.RankSpecifiers[0].Rank != 1) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.RepeatElementArrayRankConflict,
                    m.MemberDeclaration.GetLocation()));
        }

        object[] rankSizes = new object[indexExps.Length];
        for (int i = 0; i < indexExps.Length; i++) {
            rankSizes[i] = GetArraySize(indexExps[i], i, m, model, parentVar);
        }
        var rankSizeExpressions = rankSizes.Select(s => s.ToString()!).ToArray();
        var rankSizeValidationKinds = indexExps
            .Select(exp => InferValidationKindForSizeExpression(exp, context.ModelSym))
            .ToArray();
        var rankSizeValidated = EmitValidatedArrayLengths(
            deserBlock,
            rankSizeExpressions,
            $"_g_rank_{context.NextIndexId()}",
            context.Model.TypeName,
            m.MemberName,
            rankSizeValidationKinds);

        if (elementRepeating) {
            GenerateRepeatElementArray(context, seriBlock, deserBlock, eleSym, rankSizeValidated);
        }
        else {
            GenerateStandardArray(context, seriBlock, deserBlock, arr, eleSym, rankSizeExpressions, rankSizeValidated);
        }
    }

    private void GenerateLengthPrefixedRegularArray(
        TypeSerializerContext context,
        BlockNode seriBlock,
        BlockNode deserBlock,
        ArrayTypeSyntax arr,
        ITypeSymbol eleSym,
        AttributeSyntax lengthPrefixedAtt)
    {
        var m = context.Member;
        var memberAccess = context.MemberAccess;

        if (arr.RankSpecifiers[0].Rank != 1) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.LengthPrefixedArrayRankUnsupported,
                    m.MemberDeclaration.GetLocation(),
                    m.MemberName));
        }

        if (!TryResolveLengthPrefixType(lengthPrefixedAtt, out var lengthType, out var invalidTypeText)) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.LengthPrefixedArrayInvalidLengthType,
                    lengthPrefixedAtt.GetLocation(),
                    m.MemberName,
                    invalidTypeText));
        }

        var indexId = context.NextIndexId();
        var serializedLengthVar = $"_g_len_{indexId}";
        seriBlock.WriteLine($"var {serializedLengthVar} = {memberAccess}.Length;");
        if (lengthType.MaxLengthExpression is not null) {
            seriBlock.WriteLine($"if ({serializedLengthVar} > {lengthType.MaxLengthExpression}) throw new global::System.ArgumentOutOfRangeException(\"{m.MemberName}\", {serializedLengthVar}, \"Array length exceeds prefix type range.\");");
        }
        seriBlock.WriteLine($"Unsafe.Write(ptr_current, ({lengthType.WireTypeName}){serializedLengthVar});");
        seriBlock.WriteLine($"ptr_current = Unsafe.Add<{lengthType.WireTypeName}>(ptr_current, 1);");
        seriBlock.WriteLine();

        EmitLoopSegmentCheck(
            deserBlock,
            context.Model.TypeName,
            m.MemberName,
            $"sizeof({lengthType.WireTypeName})");
        var wireLengthVar = $"_g_len_wire_{indexId}";
        deserBlock.WriteLine($"var {wireLengthVar} = Unsafe.Read<{lengthType.WireTypeName}>(ptr_current);");
        deserBlock.WriteLine($"ptr_current = Unsafe.Add<{lengthType.WireTypeName}>(ptr_current, 1);");

        var rankSizeValidated = new[] {
            EmitValidatedArrayLength(
                deserBlock,
                wireLengthVar,
                $"_g_rank_{context.NextIndexId()}_0",
                context.Model.TypeName,
                m.MemberName,
                lengthType.ValidationKind)
        };
        var rankSizeExpressions = new[] { $"{memberAccess}.Length" };

        bool elementRepeating = eleSym.AllInterfaces.Any(i => i.MetadataName == "IRepeatElement`1");
        if (elementRepeating) {
            GenerateRepeatElementArray(context, seriBlock, deserBlock, eleSym, rankSizeValidated);
        }
        else {
            GenerateStandardArray(context, seriBlock, deserBlock, arr, eleSym, rankSizeExpressions, rankSizeValidated);
        }
    }

    private void GenerateRepeatElementArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        ITypeSymbol eleSym,
        string[] rankSizeValidated) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;
        var externalMemberValues = context.ExternalMemberValues;

        var indexId = context.NextIndexId();
        var oldMemberNodeCount = deserBlock.Sources.Count;

        seriBlock.Write($"for (int _g_index_{indexId} = 0; _g_index_{indexId} < {memberAccess}.Length; _g_index_{indexId}++) ");
        (seriBlock, deserBlock).BlockWrite((seriBlock, deserBlock) => {
            var roundState = context.RoundState.PushArray([$"_g_index_{indexId}"]);
            context.ExpandMembersCallback(seriBlock, deserBlock, context.ModelSym, [(m, parentVar, roundState)]);
        });
        deserBlock.Sources.RemoveRange(oldMemberNodeCount, deserBlock.Sources.Count - oldMemberNodeCount);
        seriBlock.WriteLine();

        deserBlock.WriteLine($"var _g_elementCount_{indexId} = {rankSizeValidated[0]};");
        deserBlock.WriteLine($"var _g_arrayCache_{indexId} = ArrayPool<{eleSym.Name}>.Shared.Rent(_g_elementCount_{indexId});");
        deserBlock.WriteLine($"var _g_arrayIndex_{indexId} = 0;");
        deserBlock.Write($"while(_g_elementCount_{indexId} > 0) ");
        deserBlock.BlockWrite((source) => {
            if (eleSym.IsValueType) {
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = default;");
                foreach (var (memberName, memberValue) in externalMemberValues) {
                    source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}].{memberName} = {memberValue};");
                }
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}].ReadContent(ref ptr_current, ptr_end);");
            }
            else {
                source.WriteLine($"_g_arrayCache_{indexId}[_g_arrayIndex_{indexId}] = new (ref ptr_current, ptr_end{context.ExternalMemberValueArgs});");
            }
            source.WriteLine($"var _g_repeatCountRaw_{indexId} = _g_arrayCache_{indexId}[_g_arrayIndex_{indexId}].RepeatCount;");
            source.WriteLine($"var _g_repeatCountInt64_{indexId} = (long)_g_repeatCountRaw_{indexId};");
            source.WriteLine($"if (_g_repeatCountInt64_{indexId} < 0 || _g_repeatCountInt64_{indexId} > int.MaxValue) throw new ProtocolParseException(\"Invalid repeat count in repeat-element array.\", nameof({context.Model.TypeName}), \"{m.MemberName}\", (long)ptr_current, (long)ptr_end, 0, _g_arrayIndex_{indexId}, $\"remaining={{_g_elementCount_{indexId}}};repeat={{_g_repeatCountRaw_{indexId}}}\");");
            source.WriteLine($"var _g_repeatCount_{indexId} = (int)_g_repeatCountInt64_{indexId};");
            source.WriteLine($"if (_g_repeatCount_{indexId} >= _g_elementCount_{indexId}) throw new ProtocolParseException(\"Invalid repeat count in repeat-element array.\", nameof({context.Model.TypeName}), \"{m.MemberName}\", (long)ptr_current, (long)ptr_end, 0, _g_arrayIndex_{indexId}, $\"remaining={{_g_elementCount_{indexId}}};repeat={{_g_repeatCount_{indexId}}}\");");
            source.WriteLine($"_g_elementCount_{indexId} -= _g_repeatCount_{indexId} + 1;");
            source.WriteLine($"++_g_arrayIndex_{indexId};");
        });
        deserBlock.WriteLine();
        deserBlock.WriteLine($"{memberAccess} = new {eleSym.Name}[_g_arrayIndex_{indexId}];");
        deserBlock.WriteLine($"Array.Copy(_g_arrayCache_{indexId}, {memberAccess}, _g_arrayIndex_{indexId});");
        deserBlock.WriteLine();
    }

    private void GenerateStandardArray(
        TypeSerializerContext context, BlockNode seriBlock, BlockNode deserBlock,
        ArrayTypeSyntax arr, ITypeSymbol eleSym,
        string[] rankSizeExpressions,
        string[] rankSizeValidated) {
        var m = context.Member;
        var memberAccess = context.MemberAccess;
        var parentVar = context.ParentVar;

        string[] indexNames = new string[rankSizeExpressions.Length];
        for (int i = 0; i < indexNames.Length; i++) {
            indexNames[i] = $"_g_index_{context.NextIndexId()}";
        }

        BlockNode? writeArrayBlock = null;
        BlockNode? readArrayBlock = null;
        int index = rankSizeExpressions.Length - 1;

        do {
            var indexName = indexNames[index];
            var head = $"for (int {indexName} = 0; {indexName} < {rankSizeExpressions[index]}; {indexName}++) ";

            if (writeArrayBlock is null || readArrayBlock is null) {
                writeArrayBlock = new BlockNode(seriBlock);
                readArrayBlock = new BlockNode(deserBlock);
                var roundState = context.RoundState.PushArray(indexNames);
                context.ExpandMembersCallback(writeArrayBlock, readArrayBlock, context.ModelSym, [(m, parentVar, roundState)]);
            }
            readArrayBlock.WarpBlock((head, false));
            writeArrayBlock.WarpBlock((head, false));
        }
        while (index-- > 0);

        var hasArrayCondition = m.Attributes.Any(a => a.AttributeMatch<ConditionArrayAttribute>());
        var fixedElementSizeExpression = FixedReadSizeResolver.TryGetFixedReadSizeExpressionForType(eleSym, eleSym.GetPredifinedName());
        if (fixedElementSizeExpression is not null && readArrayBlock is not null) {
            RemoveInlineEnsureReadable(readArrayBlock);

            string totalBytesExpression;
            if (hasArrayCondition && rankSizeValidated.Length == 1) {
                var precheckIndexName = $"_g_pre_idx_{context.NextIndexId()}";
                var conditionalCountVar = $"_g_cond_count_{context.NextIndexId()}";
                var precheckCondition = ConditionTreeBuilder.BuildConditionTree(m, context.ModelSym, precheckIndexName);
                var conditionExpr = precheckCondition.ToConditionExpression(parentVar, false);

                deserBlock.WriteLine($"long {conditionalCountVar} = 0;");
                deserBlock.Write($"for (int {precheckIndexName} = 0; {precheckIndexName} < {rankSizeValidated[0]}; {precheckIndexName}++) ");
                deserBlock.BlockWrite(source => {
                    source.WriteLine($"if ({conditionExpr}) ++{conditionalCountVar};");
                });
                totalBytesExpression = $"checked({conditionalCountVar} * (long)({fixedElementSizeExpression}))";
            }
            else if (rankSizeValidated.Length == 1) {
                totalBytesExpression = $"checked((long){rankSizeValidated[0]} * (long)({fixedElementSizeExpression}))";
            }
            else {
                var totalCountVar = $"_g_total_count_{context.NextIndexId()}";
                deserBlock.WriteLine($"long {totalCountVar} = {rankSizeValidated[0]};");
                for (int i = 1; i < rankSizeValidated.Length; i++) {
                    deserBlock.WriteLine($"{totalCountVar} = checked({totalCountVar} * {rankSizeValidated[i]});");
                }
                totalBytesExpression = $"checked({totalCountVar} * (long)({fixedElementSizeExpression}))";
            }

            var totalBytesVar = $"_g_total_bytes_{context.NextIndexId()}";
            deserBlock.WriteLine($"long {totalBytesVar} = {totalBytesExpression};");
            LoopReadPlanEmitter.EmitSingleSegment(
                deserBlock,
                totalBytesVar,
                context.Model.TypeName,
                m.MemberName,
                useLongExpression: true);
        }

        seriBlock.Sources.AddRange(writeArrayBlock!.Sources);
        deserBlock.WriteLine($"{memberAccess} = new {arr.ElementType}[{string.Join(", ", rankSizeValidated)}];");
        deserBlock.Sources.AddRange(readArrayBlock!.Sources);
        seriBlock.WriteLine();
    }

    private static string[] EmitValidatedArrayLengths(
        BlockNode deserBlock,
        IReadOnlyList<string> sizeExpressions,
        string variablePrefix,
        string typeName,
        string memberName,
        IReadOnlyList<ArrayLengthValidationKind>? validationHints = null)
    {
        var result = new string[sizeExpressions.Count];
        for (int i = 0; i < sizeExpressions.Count; i++) {
            var hint = validationHints is not null && i < validationHints.Count
                ? validationHints[i]
                : ArrayLengthValidationKind.Unknown;
            result[i] = EmitValidatedArrayLength(
                deserBlock,
                sizeExpressions[i],
                $"{variablePrefix}_{i}",
                typeName,
                memberName,
                hint);
        }
        return result;
    }

    private static string EmitValidatedArrayLength(
        BlockNode deserBlock,
        string sizeExpression,
        string variablePrefix,
        string typeName,
        string memberName,
        ArrayLengthValidationKind validationKind = ArrayLengthValidationKind.Unknown)
    {
        if (TryGetConstantArrayLength(sizeExpression, out var constantLength)) {
            return constantLength.ToString();
        }

        var valueVar = variablePrefix;

        if (validationKind == ArrayLengthValidationKind.NonNegativeWithinInt) {
            deserBlock.WriteLine($"var {valueVar} = (int){sizeExpression};");
            return valueVar;
        }

        if (validationKind == ArrayLengthValidationKind.CheckLowerBoundOnly) {
            deserBlock.WriteLine($"var {valueVar} = (int){sizeExpression};");
            deserBlock.WriteLine($"if ({valueVar} < 0) throw new ProtocolParseException(\"Invalid array length.\", nameof({typeName}), \"{memberName}\", (long)ptr_current, (long)ptr_end, 0, localVariables: $\"length={{{valueVar}}}\");");
            return valueVar;
        }

        var rawVar = $"{variablePrefix}_raw";
        deserBlock.WriteLine($"var {rawVar} = {sizeExpression};");
        if (validationKind == ArrayLengthValidationKind.CheckUpperBoundOnly) {
            deserBlock.WriteLine($"if ({rawVar} > int.MaxValue) throw new ProtocolParseException(\"Invalid array length.\", nameof({typeName}), \"{memberName}\", (long)ptr_current, (long)ptr_end, 0, localVariables: $\"length={{{rawVar}}}\");");
            deserBlock.WriteLine($"var {valueVar} = (int){rawVar};");
            return valueVar;
        }

        if (validationKind == ArrayLengthValidationKind.CheckLowerAndUpperBound) {
            deserBlock.WriteLine($"if ({rawVar} < 0 || {rawVar} > int.MaxValue) throw new ProtocolParseException(\"Invalid array length.\", nameof({typeName}), \"{memberName}\", (long)ptr_current, (long)ptr_end, 0, localVariables: $\"length={{{rawVar}}}\");");
            deserBlock.WriteLine($"var {valueVar} = (int){rawVar};");
            return valueVar;
        }

        deserBlock.WriteLine($"if ((long){rawVar} < 0 || (long){rawVar} > int.MaxValue) throw new ProtocolParseException(\"Invalid array length.\", nameof({typeName}), \"{memberName}\", (long)ptr_current, (long)ptr_end, 0, localVariables: $\"length={{{rawVar}}}\");");
        deserBlock.WriteLine($"var {valueVar} = (int)(long){rawVar};");
        return valueVar;
    }

    private readonly record struct LengthPrefixTypeInfo(string WireTypeName, string? MaxLengthExpression, ArrayLengthValidationKind ValidationKind);

    private static bool TryResolveLengthPrefixType(
        AttributeSyntax lengthPrefixedAtt,
        out LengthPrefixTypeInfo typeInfo,
        out string invalidTypeText)
    {
        typeInfo = default;
        invalidTypeText = "<missing>";

        var parameters = lengthPrefixedAtt.ExtractAttributeParams();
        if (parameters.Length != 1 || parameters[0] is not TypeOfExpressionSyntax typeOfExpression) {
            invalidTypeText = parameters.Length == 0 ? "<missing>" : parameters[0].ToString();
            return false;
        }

        invalidTypeText = typeOfExpression.Type.ToString();
        var normalizedTypeName = NormalizeLengthPrefixTypeName(typeOfExpression.Type.ToString());
        switch (normalizedTypeName) {
            case "byte":
            case "Byte":
                typeInfo = new LengthPrefixTypeInfo("byte", "byte.MaxValue", ArrayLengthValidationKind.NonNegativeWithinInt);
                return true;
            case "sbyte":
            case "SByte":
                typeInfo = new LengthPrefixTypeInfo("sbyte", "sbyte.MaxValue", ArrayLengthValidationKind.CheckLowerBoundOnly);
                return true;
            case "short":
            case "Int16":
                typeInfo = new LengthPrefixTypeInfo("short", "short.MaxValue", ArrayLengthValidationKind.CheckLowerBoundOnly);
                return true;
            case "ushort":
            case "UInt16":
                typeInfo = new LengthPrefixTypeInfo("ushort", "ushort.MaxValue", ArrayLengthValidationKind.NonNegativeWithinInt);
                return true;
            case "int":
            case "Int32":
                typeInfo = new LengthPrefixTypeInfo("int", null, ArrayLengthValidationKind.CheckLowerBoundOnly);
                return true;
            case "uint":
            case "UInt32":
                typeInfo = new LengthPrefixTypeInfo("uint", null, ArrayLengthValidationKind.CheckUpperBoundOnly);
                return true;
            default:
                return false;
        }
    }

    private static string NormalizeLengthPrefixTypeName(string typeName)
    {
        var normalized = typeName;
        const string globalPrefix = "global::";
        if (normalized.StartsWith(globalPrefix, StringComparison.Ordinal)) {
            normalized = normalized.Substring(globalPrefix.Length);
        }
        const string systemPrefix = "System.";
        if (normalized.StartsWith(systemPrefix, StringComparison.Ordinal)) {
            return normalized.Substring(systemPrefix.Length);
        }

        return normalized;
    }

    private static ArrayLengthValidationKind InferValidationKindForSizeExpression(ExpressionSyntax expression, INamedTypeSymbol modelSym)
    {
        if (!TryExtractSizeMemberName(expression, out var memberName)) {
            return ArrayLengthValidationKind.Unknown;
        }

        var member = modelSym.GetMembers(memberName).FirstOrDefault(m => m is IFieldSymbol or IPropertySymbol);
        if (member is IFieldSymbol field) {
            return GetValidationKindForType(field.Type);
        }
        if (member is IPropertySymbol property) {
            return GetValidationKindForType(property.Type);
        }
        return ArrayLengthValidationKind.Unknown;
    }

    private static bool TryExtractSizeMemberName(ExpressionSyntax expression, out string memberName)
    {
        memberName = string.Empty;

        if (expression is LiteralExpressionSyntax lit) {
            var text = lit.Token.Text;
            if (text.Length >= 2 && text[0] == '"' && text[^1] == '"') {
                memberName = text.Substring(1, text.Length - 2);
                return memberName.Length > 0;
            }
            return false;
        }

        if (expression is InvocationExpressionSyntax invocation
            && invocation.Expression.ToString() == "nameof"
            && invocation.ArgumentList.Arguments.Count == 1) {
            var targetText = invocation.ArgumentList.Arguments[0].Expression.ToString();
            if (string.IsNullOrWhiteSpace(targetText)) {
                return false;
            }

            var dotIndex = targetText.LastIndexOf('.');
            memberName = dotIndex >= 0 ? targetText.Substring(dotIndex + 1) : targetText;
            return memberName.Length > 0;
        }

        return false;
    }

    private static ArrayLengthValidationKind GetValidationKindForType(ITypeSymbol symbol)
    {
        if (symbol is INamedTypeSymbol enumSymbol && enumSymbol.TypeKind == TypeKind.Enum && enumSymbol.EnumUnderlyingType is not null) {
            symbol = enumSymbol.EnumUnderlyingType;
        }

        return symbol.SpecialType switch {
            SpecialType.System_Byte => ArrayLengthValidationKind.NonNegativeWithinInt,
            SpecialType.System_UInt16 => ArrayLengthValidationKind.NonNegativeWithinInt,
            SpecialType.System_SByte => ArrayLengthValidationKind.CheckLowerBoundOnly,
            SpecialType.System_Int16 => ArrayLengthValidationKind.CheckLowerBoundOnly,
            SpecialType.System_Int32 => ArrayLengthValidationKind.CheckLowerBoundOnly,
            SpecialType.System_UInt32 => ArrayLengthValidationKind.CheckUpperBoundOnly,
            SpecialType.System_Int64 => ArrayLengthValidationKind.CheckLowerAndUpperBound,
            SpecialType.System_UInt64 => ArrayLengthValidationKind.CheckUpperBoundOnly,
            _ => ArrayLengthValidationKind.Unknown
        };
    }

    private static bool TryGetConstantArrayLength(string sizeExpression, out int constantLength)
    {
        constantLength = default;

        var expression = UnwrapParentheses(SyntaxFactory.ParseExpression(sizeExpression));
        if (expression is not LiteralExpressionSyntax literal || !literal.IsKind(SyntaxKind.NumericLiteralExpression)) {
            return false;
        }

        return TryConvertToNonNegativeInt(literal.Token.Value, out constantLength);
    }

    private static ExpressionSyntax UnwrapParentheses(ExpressionSyntax expression)
    {
        while (expression is ParenthesizedExpressionSyntax parenthesized) {
            expression = parenthesized.Expression;
        }

        return expression;
    }

    private static bool TryConvertToNonNegativeInt(object? value, out int converted)
    {
        converted = default;

        if (value is null) {
            return false;
        }

        switch (value) {
            case int i when i >= 0:
                converted = i;
                return true;
            case uint u when u <= int.MaxValue:
                converted = (int)u;
                return true;
            case long l when l >= 0 && l <= int.MaxValue:
                converted = (int)l;
                return true;
            case ulong ul when ul <= int.MaxValue:
                converted = (int)ul;
                return true;
            case short s when s >= 0:
                converted = s;
                return true;
            case ushort us:
                converted = us;
                return true;
            case byte b:
                converted = b;
                return true;
            case sbyte sb when sb >= 0:
                converted = sb;
                return true;
            default:
                return false;
        }
    }

    private static void EmitLoopSegmentCheck(
        BlockNode source,
        string typeName,
        string memberName,
        string sizeExpression,
        string? loopIndexExpression = null,
        bool useLongExpression = false)
    {
        LoopReadPlanEmitter.EmitSingleSegment(
            source,
            sizeExpression,
            typeName,
            memberName,
            loopIndexExpression,
            useLongExpression);
    }

    private static void RemoveInlineEnsureReadable(SourceGroup group)
    {
        for (int i = group.Sources.Count - 1; i >= 0; i--) {
            if (group.Sources[i] is SourceGroup nested) {
                RemoveInlineEnsureReadable(nested);
                continue;
            }

            if (group.Sources[i] is not NewLineTextNode line) {
                continue;
            }

            if (line.Code.Contains("CommonCode.EnsureReadable(ptr_current, ptr_end", StringComparison.Ordinal)
                || line.Code is "#if DEBUG" or "#else" or "#endif") {
                group.Sources.RemoveAt(i);
            }
        }
    }

    private static string ExtractSizeExpression(ExpressionSyntax expr, string? parentVar) {
        if (expr is LiteralExpressionSyntax lit) {
            return lit.ToString();
        }
        if (expr is InvocationExpressionSyntax invo && invo.Expression.ToString() == "nameof") {
            var name = invo.ArgumentList.Arguments.First().Expression.ToString();
            return (parentVar is null ? "" : $"{parentVar}.") + name;
        }
        return expr.ToString();
    }

    private static object GetArraySize(ExpressionSyntax indexExp, int i, SerializationExpandContext m, ProtocolTypeData model, string? parentVar) {
        object? size = null;
        if (indexExp is LiteralExpressionSyntax lit) {
            var text = lit.Token.Text;
            if (text.StartsWith("\"") && text.EndsWith("\"")) {
                size = (parentVar is null ? "" : $"{parentVar}.") + text.Substring(1, text.Length - 2);
            }
            else if (ushort.TryParse(text, out var numSize)) {
                size = numSize;
            }
        }
        else if (indexExp is InvocationExpressionSyntax inv && inv.Expression.ToString() == "nameof") {
            size = (parentVar is null ? "" : $"{parentVar}.") + inv.ArgumentList.Arguments.First().Expression;
        }

        if (size == null) {
            throw new DiagnosticException(
                Diagnostic.Create(
                    DiagnosticDescriptors.ArrayRankSizeInvalid,
                    m.MemberDeclaration.GetLocation(),
                    i,
                    m.MemberName,
                    model.TypeName));
        }
        return size;
    }
}
