using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrProtocol.Attributes;
using TrProtocol.Interfaces;
using TrProtocol.SerializerGenerator.Internal.Conditions.Analysis;
using TrProtocol.SerializerGenerator.Internal.Conditions.CodeGeneration;
using TrProtocol.SerializerGenerator.Internal.Conditions.Model;
using TrProtocol.SerializerGenerator.Internal.Conditions.Optimization;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;
using TrProtocol.SerializerGenerator.Internal.ReadPlan;
using TrProtocol.SerializerGenerator.Internal.Serialization;
using TrProtocol.SerializerGenerator.Internal.Serialization.TypeSerializers;
using TrProtocol.SerializerGenerator.Internal.Utilities;

namespace TrProtocol.SerializerGenerator.Internal.Generation;

internal static class MemberSerializationEmitter
{
    public static void Emit(
        SourceProductionContext context,
        TypeSerializerDispatcher typeSerializerDispatcher,
        ProtocolTypeData model,
        INamedTypeSymbol modelSym,
        List<string> memberNullables,
        BlockNode seriNode,
        BlockNode deserNode,
        Func<TypeDeclarationSyntax, ProtocolTypeInfo> transform) {
        var indexIdRef = new Ref<int>(0);

        void ExpandMembers(
            BlockNode seriNode,
            BlockNode deserNode,
            INamedTypeSymbol typeSym,
            IEnumerable<(SerializationExpandContext m, string? parant_var, RoundState roundState)> memberAccesses) {
            void EmitMergedMembers(IReadOnlyList<ReadPlanMember> generatedMembers) {
                if (generatedMembers.Count == 0) return;

                var currentParentVar = (string?)null;
                var buffer = new List<ReadPlanMember>();

                void Flush() {
                    if (buffer.Count == 0) return;

                    var blocks = AdjacentFieldMerger.MergeAdjacentConditions(
                        buffer.Select(e => (e.Member, e.Condition, e.ParentVar)));

                    var membersByContext = new Dictionary<SerializationExpandContext, ReadPlanMember>(buffer.Count);
                    var memberSources = new Dictionary<SerializationExpandContext, (IReadOnlyList<SourceNode> seriSources, IReadOnlyList<SourceNode> deserSources)>(buffer.Count);
                    foreach (var e in buffer) {
                        membersByContext[e.Member] = e;
                        memberSources[e.Member] = (e.SerializationBlock.Sources, e.DeserializationBlock.Sources);
                    }

                    var readPlan = ReadPlanBuilder.Build(blocks, membersByContext);
                    ReadPlanEmitter.Emit(readPlan, model.TypeName);

                    var seriTemp = new BlockNode(seriNode);
                    var deserTemp = new BlockNode(deserNode);
                    foreach (var access in buffer
                        .Where(entry => !entry.Condition.IsEmpty)
                        .Select(entry => entry.ParentVar is null
                            ? entry.Member.MemberName
                            : $"{entry.ParentVar}.{entry.Member.MemberName}")
                        .Distinct(StringComparer.Ordinal)) {
                        // A protocol object can be reused for multiple reads. Optional members must not retain a
                        // value from a previous frame when their current condition is false.
                        deserTemp.WriteLine($"{access} = default;");
                    }
                    ConditionBlockCodeGenerator.GenerateConditionBlocks(blocks, memberSources, seriTemp, deserTemp, currentParentVar);
                    seriNode.Sources.AddRange(seriTemp.Sources);
                    deserNode.Sources.AddRange(deserTemp.Sources);

                    buffer.Clear();
                }

                foreach (var e in generatedMembers) {
                    if (buffer.Count == 0) {
                        currentParentVar = e.ParentVar;
                    }
                    else if (e.ParentVar != currentParentVar) {
                        Flush();
                        currentParentVar = e.ParentVar;
                    }

                    buffer.Add(e);
                }

                Flush();
            }

            try {
                var generatedMembers = new List<ReadPlanMember>();
                foreach (var (m, parant_var, roundState) in memberAccesses) {
                    var mType = m.MemberType;
                    var mTypeStr = mType.ToString();
                    MemberSymbolResolver.ResolveMemberSymbol(typeSym, m, out var memberTypeSym, out var fieldMemberSym, out var propMemberSym);

                    string memberAccess;

                    if (parant_var == null) {
                        memberAccess = m.MemberName;
                    }
                    else {
                        memberAccess = $"{parant_var}.{m.MemberName}";

                        var memberNameSpace = memberTypeSym.GetFullNamespace();
                        if (!string.IsNullOrEmpty(memberNameSpace) && !model.Imports.Contains(memberNameSpace)) {
                            model.Imports.Add(memberNameSpace);
                        }
                    }

                    if (roundState.IsArrayRound) {
                        mType = ((ArrayTypeSyntax)mType).ElementType;
                        memberTypeSym = ((IArrayTypeSymbol)memberTypeSym).ElementType;

                        if (mType is NullableTypeSyntax) {
                            throw new DiagnosticException(
                                Diagnostic.Create(
                                    new DiagnosticDescriptor(
                                        "SCG22",
                                        "invaild array element DefSymbol",
                                        "The element type of an array type member '{0}' of type '{1}' cannot be nullable '{2}'",
                                        "",
                                        DiagnosticSeverity.Error,
                                        true),
                                    mType.GetLocation(),
                                    m.MemberName,
                                    model.TypeName,
                                    mType.ToString()));
                        }

                        mTypeStr = mType.ToString();
                        memberAccess = $"{memberAccess}[{string.Join(",", roundState.IndexNames)}]";
                    }
                    if (roundState.IsEnumRound) {
                        mTypeStr = roundState.EnumType.underlyingType.GetPredifinedName();
                    }
                    var seriMemberBlock = new BlockNode(seriNode);
                    var deserMemberBlock = new BlockNode(deserNode);

                    ConditionNode memberCondition;
                    var isConditionalFlag = false;
                    if (roundState.IsArrayRound && !roundState.IsEnumRound) {
                        var arrayConditionAttr = m.Attributes.FirstOrDefault(a => a.AttributeMatch<ConditionArrayAttribute>());
                        if (arrayConditionAttr is null) {
                            memberCondition = EmptyConditionNode.Instance;
                        }
                        else {
                            if (roundState.IndexNames.Length != 1) {
                                throw new DiagnosticException(
                                    Diagnostic.Create(
                                        DiagnosticDescriptors.ArrayConditionOnlyOneDimensional,
                                        arrayConditionAttr.GetLocation()));
                            }

                            memberCondition = ConditionTreeBuilder.BuildConditionTree(m, typeSym, roundState.IndexNames[0]);
                        }
                    }
                    else if (roundState.IsArrayRound || roundState.IsEnumRound) {
                        memberCondition = EmptyConditionNode.Instance;
                    }
                    else {
                        var conditionTree = ConditionTreeBuilder.BuildConditionTree(m, typeSym);
                        if (!conditionTree.IsEmpty) {
                            isConditionalFlag = true;
                            if (ConditionTreeBuilder.RequiresNullableType(conditionTree, memberTypeSym)) {
                                throw new DiagnosticException(
                                    Diagnostic.Create(
                                        DiagnosticDescriptors.ConditionalMemberMustBeNullable,
                                        m.MemberType.GetLocation(),
                                        m.MemberName));
                            }
                        }

                        bool hasC2S = m.Attributes.Any(a => a.AttributeMatch<C2SOnlyAttribute>());
                        bool hasS2C = m.Attributes.Any(a => a.AttributeMatch<S2COnlyAttribute>());
                        if (hasC2S && hasS2C) {
                            var conflictAttr = m.Attributes.FirstOrDefault(a => a.AttributeMatch<S2COnlyAttribute>());
                            throw new DiagnosticException(
                                Diagnostic.Create(
                                    DiagnosticDescriptors.ConflictingSideAttributes,
                                    conflictAttr?.GetLocation() ?? m.MemberDeclaration.GetLocation()));
                        }

                        if (hasC2S || hasS2C) {
                            if (!typeSym.AllInterfaces.Any(i => i.Name == nameof(ISideSpecific))) {
                                throw new DiagnosticException(
                                    Diagnostic.Create(
                                        DiagnosticDescriptors.SideSpecificRequiresInterface,
                                        m.MemberDeclaration.GetLocation()));
                            }

                            var side = new SideConditionNode(IsC2SOnly: hasC2S);
                            memberCondition = conditionTree.IsEmpty ? side : new AndConditionNode(side, conditionTree);
                        }
                        else {
                            memberCondition = conditionTree;
                        }
                    }

                    var ownerMemberSymbol = (ISymbol?)fieldMemberSym ?? propMemberSym!;
                    List<(string memberName, string memberValue)> externalMemberValues = ExternalMemberValueExtractor.Extract(
                        m,
                        memberTypeSym);
                    externalMemberValues.AddRange(ExternalMemberValueEqualExtractor.Extract(
                        m,
                        memberTypeSym,
                        typeSym,
                        ownerMemberSymbol,
                        parant_var,
                        externalMemberValues.Select(value => value.memberName)));
                    string externalMemberValueArgs = "";
                    foreach (var (memberName, memberValue) in externalMemberValues) {
                        externalMemberValueArgs += $", _{memberName}: {memberValue}";
                    }

                    var typeSerializerContext = new TypeSerializerContext(
                        m,
                        memberTypeSym,
                        fieldMemberSym,
                        propMemberSym,
                        memberAccess,
                        parant_var,
                        model,
                        typeSym,
                        mTypeStr,
                        roundState,
                        isConditionalFlag,
                        externalMemberValueArgs,
                        externalMemberValues,
                        memberNullables,
                        indexIdRef,
                        ExpandMembers,
                        transform);

                    SerializeAsTypeValidator.Validate(typeSerializerContext);
                    var fixedSizeExpression = roundState.IsEnumRound
                        ? null
                        : FixedReadSizeResolver.TryGetFixedReadSizeExpression(typeSerializerContext);
                    typeSerializerDispatcher.Serialize(typeSerializerContext, seriMemberBlock, deserMemberBlock);
                    generatedMembers.Add(new ReadPlanMember(
                        m,
                        memberCondition,
                        parant_var,
                        fixedSizeExpression is null ? ReadPlanNodeKind.Variable : ReadPlanNodeKind.Fixed,
                        fixedSizeExpression,
                        seriMemberBlock,
                        deserMemberBlock));

                }

                EmitMergedMembers(generatedMembers);
            }
            catch (DiagnosticException de) {
                context.ReportDiagnostic(de.Diagnostic);
                return;
            }
        }

        ExpandMembers(
            seriNode,
            deserNode,
            modelSym,
            model.Members.Select<SerializationExpandContext, (SerializationExpandContext, string?, RoundState)>(m => (m, null, RoundState.Empty)));
    }

}
