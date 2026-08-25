using Microsoft.CodeAnalysis;
using TrProtocol.Interfaces;

namespace TrProtocol.SerializerGenerator.Internal.Diagnostics;

/// <summary>
/// Diagnostic descriptors for the condition system.
/// </summary>
public static class DiagnosticDescriptors
{
    private const string Category = "TrProtocol.SerializerGenerator";

    /// <summary>
    /// SCG19: Conditional members must be declared nullable.
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionalMemberMustBeNullable = new(
        "SCG19",
        "Conditional member must be nullable",
        "Reference type member '{0}' marked as conditional serializations must be declared nullable",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG03: C2SOnly and S2COnly cannot exist at the same time.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingSideAttributes = new(
        "SCG03",
        "Conflicting side attributes",
        "Only one of C2SOnly and S2COnly can exist at the same time",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG04: C2SOnly/S2COnly requires the declaring type to implement ISideSpecific.
    /// </summary>
    public static readonly DiagnosticDescriptor SideSpecificRequiresInterface = new(
        "SCG04",
        "Side-specific member requires ISideSpecific",
        $"C2SOnly and S2COnly can only be annotated on members of types that implement the {nameof(ISideSpecific)} interface",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG43: IncludeSerialize and IgnoreSerialize cannot exist at the same time.
    /// </summary>
    public static readonly DiagnosticDescriptor ConflictingIncludeIgnoreSerializeAttributes = new(
        "SCG43",
        "Conflicting serialization attributes",
        "Member '{0}' in type '{1}' cannot declare both IncludeSerializeAttribute and IgnoreSerializeAttribute",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG20: ConditionArray can only be used on one-dimensional arrays.
    /// </summary>
    public static readonly DiagnosticDescriptor ArrayConditionOnlyOneDimensional = new(
        "SCG20",
        "ArrayCondition only supports 1D arrays",
        "ArrayConditionAttribute is only allowed to be applied to members of the one-dimensional array type",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG14: Indexed condition member must expose a readable Boolean Int32 indexer.
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionMemberMustHaveBooleanIntIndexer = new(
        "SCG14",
        "Condition member must expose a Boolean Int32 indexer",
        "Condition member '{0}' must expose a readable Boolean indexer with one Int32 parameter",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG15: Boolean condition attribute is invalid.
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionMemberMustBeBoolean = new(
        "SCG15",
        "Condition member must be Boolean",
        "arg1 of condition attribute must be name of field or property which type is {0}",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG16: Condition attribute arguments are invalid.
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionAttributeArgumentInvalid = new(
        "SCG16",
        "Condition attribute argument invalid",
        "condition attribute argument of member '{0}' model '{1}' is invalid",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG17: Condition comparison member was not found.
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionComparisonMemberNotFound = new(
        "SCG17",
        "Condition comparison member not found",
        "arg1 of condition attribute must be name of field or property",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG18: Condition comparison attribute arguments are invalid.
    /// </summary>
    public static readonly DiagnosticDescriptor ConditionComparisonArgumentInvalid = new(
        "SCG18",
        "Condition comparison argument invalid",
        "condition attribute argument of member '{0}' model '{1}' is invalid",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG21: Array condition attribute arguments are invalid.
    /// </summary>
    public static readonly DiagnosticDescriptor ArrayConditionArgumentInvalid = new(
        "SCG21",
        "Array condition attribute argument invalid",
        "array condition attribute argument of member '{0}' model '{1}' is invalid",
        Category,
        DiagnosticSeverity.Error,
        true);

    // ============== TypeSerializer strategy diagnostics ==============

    /// <summary>
    /// SCG22: Array element type cannot itself be an array (jagged arrays are not supported).
    /// </summary>
    public static readonly DiagnosticDescriptor ArrayElementShouldNotBeArray = new(
        "SCG22",
        "invalid array type",
        "Array member '{1}' in type '{0}' has nested array element which is not supported",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG23: Array is missing ArraySizeAttribute.
    /// </summary>
    public static readonly DiagnosticDescriptor ArraySizeMissing = new(
        "SCG23",
        "missing array size",
        "Array member '{0}' in type '{1}' must have an ArraySizeAttribute",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG24: Array rank does not match ArraySizeAttribute.
    /// </summary>
    public static readonly DiagnosticDescriptor ArrayRankConflict = new(
        "SCG24",
        "array rank mismatch",
        "Array member '{0}' in type '{1}' has mismatched rank with ArraySizeAttribute",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG25: Array rank size expression is invalid.
    /// </summary>
    public static readonly DiagnosticDescriptor ArrayRankSizeInvalid = new(
        "SCG25",
        "invalid array size",
        "Rank {0} of array member '{1}' in type '{2}' has invalid size expression",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG26: IRepeatElement arrays must be one-dimensional.
    /// </summary>
    public static readonly DiagnosticDescriptor RepeatElementArrayRankConflict = new(
        "SCG26",
        "repeat element array rank",
        "IRepeatElement array must be one-dimensional",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG32: Int7BitEncoded member has invalid type.
    /// </summary>
    public static readonly DiagnosticDescriptor Int7BitEncodedMemberInvalidType = new(
        "SCG32",
        "invalid 7bit encoded member type",
        "Member '{0}' marked with Int7BitEncodedAttribute must be an int, an int32-backed enum, or an array of those types, but was '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG34: Int7BitEncoded arrays do not support multi-dimensional arrays.
    /// </summary>
    public static readonly DiagnosticDescriptor Int7BitEncodedArrayMultiDim = new(
        "SCG34",
        "7bit array multi-dimensional",
        "Array member '{0}' marked with Int7BitEncodedAttribute must be one-dimensional, but has rank {1}",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG35: SparseArray is missing the size parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor SparseArrayMissingSize = new(
        "SCG35",
        "sparse array missing size",
        "SparseArrayAttribute on member '{0}' must have a size parameter",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG36: SparseArray only supports numeric element types.
    /// </summary>
    public static readonly DiagnosticDescriptor SparseArrayInvalidType = new(
        "SCG36",
        "sparse array invalid type",
        "SparseArrayAttribute on member '{0}' only supports numeric element types, but was '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG37: TerminatedArray is missing the maxCount parameter.
    /// </summary>
    public static readonly DiagnosticDescriptor TerminatedArrayMissingMaxCount = new(
        "SCG37",
        "terminated array missing maxCount",
        "TerminatedArrayAttribute on member '{0}' must have a maxCount parameter",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG38: TerminatedArray element type must be supported.
    /// </summary>
    public static readonly DiagnosticDescriptor TerminatedArrayInvalidElementType = new(
        "SCG38",
        "terminated array invalid element type",
        "TerminatedArrayAttribute on member '{0}' must have element type '{1}' as a supported primitive/enum or a struct with a supported primitive/enum termination key, but was '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG39: TerminatedArray element type must be inline-expandable.
    /// </summary>
    public static readonly DiagnosticDescriptor TerminatedArrayElementNotInlineExpandable = new(
        "SCG39",
        "terminated array element not inline-expandable",
        "TerminatedArrayAttribute on member '{0}' requires element type '{1}' to be available for inline expansion (no type definition found in current compilation)",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG40: TerminatedArray termination key must be the first serializable member and supported.
    /// </summary>
    public static readonly DiagnosticDescriptor TerminatedArrayInvalidTerminationKey = new(
        "SCG40",
        "terminated array invalid termination key",
        "TerminatedArrayAttribute on member '{0}' requires the first serializable public member '{1}' of element type '{2}' to be a supported primitive/bool/char or enum-backed primitive, but was '{3}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG41: TerminatedArray element type cannot use custom serialization interfaces.
    /// </summary>
    public static readonly DiagnosticDescriptor TerminatedArrayElementHasCustomSerializer = new(
        "SCG41",
        "terminated array element has custom serializer",
        "TerminatedArrayAttribute on member '{0}' does not support element type '{1}' because it implements a custom serialization interface",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG42: TerminatedArray termination key member cannot use SerializeAs.
    /// </summary>
    public static readonly DiagnosticDescriptor TerminatedArrayKeyMemberSerializeAsNotSupported = new(
        "SCG42",
        "terminated array key SerializeAs not supported",
        "TerminatedArrayAttribute on member '{0}' does not support SerializeAsAttribute on termination key member '{1}' of element type '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG44: Length-prefixed arrays only support one-dimensional arrays.
    /// </summary>
    public static readonly DiagnosticDescriptor LengthPrefixedArrayRankUnsupported = new(
        "SCG44",
        "length-prefixed array rank unsupported",
        "LengthPrefixedArrayAttribute on member '{0}' only supports one-dimensional arrays",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG45: Length-prefixed array requires an integral prefix type up to uint.
    /// </summary>
    public static readonly DiagnosticDescriptor LengthPrefixedArrayInvalidLengthType = new(
        "SCG45",
        "invalid length-prefixed array length type",
        "LengthPrefixedArrayAttribute on member '{0}' requires length type byte/sbyte/short/ushort/int/uint, but was '{1}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG46: LengthPrefixedArray and ArraySize cannot be used together.
    /// </summary>
    public static readonly DiagnosticDescriptor LengthPrefixedArrayConflictsWithArraySize = new(
        "SCG46",
        "conflicting array length attributes",
        "Member '{0}' cannot declare both LengthPrefixedArrayAttribute and ArraySizeAttribute",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG47: LengthPrefixedArray cannot be combined with array format attributes that define independent framing.
    /// </summary>
    public static readonly DiagnosticDescriptor LengthPrefixedArrayUnsupportedCombination = new(
        "SCG47",
        "unsupported length-prefixed array attribute combination",
        "LengthPrefixedArrayAttribute on member '{0}' cannot be combined with {1}",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG50: ExternalMemberValueEqual arguments are malformed or ambiguous.
    /// </summary>
    public static readonly DiagnosticDescriptor ExternalMemberValueEqualArgumentsInvalid = new(
        "SCG50",
        "invalid external member equality binding",
        "ExternalMemberValueEqualAttribute on member '{0}' has invalid or duplicate arguments",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG51: The owner source member was not found.
    /// </summary>
    public static readonly DiagnosticDescriptor ExternalMemberValueEqualSourceNotFound = new(
        "SCG51",
        "external equality source member not found",
        "ExternalMemberValueEqualAttribute on member '{0}' cannot find source member '{1}' on owner type '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG52: The nested target member was not found.
    /// </summary>
    public static readonly DiagnosticDescriptor ExternalMemberValueEqualTargetNotFound = new(
        "SCG52",
        "external equality target member not found",
        "ExternalMemberValueEqualAttribute on member '{0}' cannot find target member '{1}' on nested type '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG53: The nested target must opt in as an external member.
    /// </summary>
    public static readonly DiagnosticDescriptor ExternalMemberValueEqualTargetMustBeExternal = new(
        "SCG53",
        "external equality target must be external",
        "Target member '{0}' on nested type '{1}' must be marked with ExternalMemberAttribute",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG54: Equality bindings can only feed boolean external members.
    /// </summary>
    public static readonly DiagnosticDescriptor ExternalMemberValueEqualTargetMustBeBoolean = new(
        "SCG54",
        "external equality target must be boolean",
        "Target member '{0}' on nested type '{1}' must be Boolean, but was '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG55: The equality source must already have been read from the wire.
    /// </summary>
    public static readonly DiagnosticDescriptor ExternalMemberValueEqualSourceMustPrecedeTarget = new(
        "SCG55",
        "external equality source must precede target",
        "Source member '{0}' must be a serializable member declared before target member '{1}' on owner type '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG56: The expected constant must have the source member's type.
    /// </summary>
    public static readonly DiagnosticDescriptor ExternalMemberValueEqualExpectedTypeMismatch = new(
        "SCG56",
        "external equality expected value type mismatch",
        "Expected value for source member '{0}' must have type '{1}', but was '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG57: The generator must be able to assign the nested external member before writing.
    /// </summary>
    public static readonly DiagnosticDescriptor ExternalMemberValueEqualTargetNotWritable = new(
        "SCG57",
        "external equality target is not writable",
        "Target member '{0}' on nested type '{1}' must be writable by generated serialization code",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG58: SerializeAs only supports primitive numeric wire conversions.
    /// </summary>
    public static readonly DiagnosticDescriptor SerializeAsRequiresNumericTypes = new(
        "SCG58",
        "SerializeAs requires numeric types",
        "SerializeAsAttribute on member '{0}' only supports primitive numeric sources and targets, but received '{1}' -> '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true);

    /// <summary>
    /// SCG31: Unsupported member type.
    /// </summary>
    public static readonly DiagnosticDescriptor UnsupportedMemberType = new(
        "SCG31",
        "unsupported member type",
        "Generating serialization for member '{0}' of type '{1}' encountered unsupported member type '{2}'",
        Category,
        DiagnosticSeverity.Error,
        true);
}
