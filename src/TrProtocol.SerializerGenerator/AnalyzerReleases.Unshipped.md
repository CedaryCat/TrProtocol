; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|-------
SCG03 | TrProtocol.SerializerGenerator | Error | ConflictingSideAttributes
SCG04 | TrProtocol.SerializerGenerator | Error | SideSpecificRequiresInterface
SCG14 | TrProtocol.SerializerGenerator | Error | ConditionMemberMustHaveBooleanIntIndexer
SCG15 | TrProtocol.SerializerGenerator | Error | ConditionMemberMustBeBoolean
SCG16 | TrProtocol.SerializerGenerator | Error | ConditionAttributeArgumentInvalid
SCG17 | TrProtocol.SerializerGenerator | Error | ConditionComparisonMemberNotFound
SCG18 | TrProtocol.SerializerGenerator | Error | ConditionComparisonArgumentInvalid
SCG19 | TrProtocol.SerializerGenerator | Error | ConditionalMemberMustBeNullable
SCG20 | TrProtocol.SerializerGenerator | Error | ArrayConditionOnlyOneDimensional
SCG21 | TrProtocol.SerializerGenerator | Error | ArrayConditionArgumentInvalid
SCG22 | TrProtocol.SerializerGenerator | Error | ArrayElementShouldNotBeArray
SCG23 | TrProtocol.SerializerGenerator | Error | ArraySizeMissing
SCG24 | TrProtocol.SerializerGenerator | Error | ArrayRankConflict
SCG25 | TrProtocol.SerializerGenerator | Error | ArrayRankSizeInvalid
SCG26 | TrProtocol.SerializerGenerator | Error | RepeatElementArrayRankConflict
SCG31 | TrProtocol.SerializerGenerator | Error | UnsupportedMemberType
SCG32 | TrProtocol.SerializerGenerator | Error | Int7BitEncodedMemberInvalidType
SCG34 | TrProtocol.SerializerGenerator | Error | Int7BitEncodedArrayMultiDim
SCG35 | TrProtocol.SerializerGenerator | Error | SparseArrayMissingSize
SCG36 | TrProtocol.SerializerGenerator | Error | SparseArrayInvalidType
SCG37 | TrProtocol.SerializerGenerator | Error | TerminatedArrayMissingMaxCount
SCG38 | TrProtocol.SerializerGenerator | Error | TerminatedArrayInvalidElementType
SCG39 | TrProtocol.SerializerGenerator | Error | TerminatedArrayElementNotInlineExpandable
SCG40 | TrProtocol.SerializerGenerator | Error | TerminatedArrayInvalidTerminationKey
SCG41 | TrProtocol.SerializerGenerator | Error | TerminatedArrayElementHasCustomSerializer
SCG42 | TrProtocol.SerializerGenerator | Error | TerminatedArrayKeyMemberSerializeAsNotSupported
SCG43 | TrProtocol.SerializerGenerator | Error | ConflictingIncludeIgnoreSerializeAttributes
SCG44 | TrProtocol.SerializerGenerator | Error | LengthPrefixedArrayRankUnsupported
SCG45 | TrProtocol.SerializerGenerator | Error | LengthPrefixedArrayInvalidLengthType
SCG46 | TrProtocol.SerializerGenerator | Error | LengthPrefixedArrayConflictsWithArraySize
SCG47 | TrProtocol.SerializerGenerator | Error | LengthPrefixedArrayUnsupportedCombination
SCG50 | TrProtocol.SerializerGenerator | Error | ExternalMemberValueEqualArgumentsInvalid
SCG51 | TrProtocol.SerializerGenerator | Error | ExternalMemberValueEqualSourceNotFound
SCG52 | TrProtocol.SerializerGenerator | Error | ExternalMemberValueEqualTargetNotFound
SCG53 | TrProtocol.SerializerGenerator | Error | ExternalMemberValueEqualTargetMustBeExternal
SCG54 | TrProtocol.SerializerGenerator | Error | ExternalMemberValueEqualTargetMustBeBoolean
SCG55 | TrProtocol.SerializerGenerator | Error | ExternalMemberValueEqualSourceMustPrecedeTarget
SCG56 | TrProtocol.SerializerGenerator | Error | ExternalMemberValueEqualExpectedTypeMismatch
SCG57 | TrProtocol.SerializerGenerator | Error | ExternalMemberValueEqualTargetNotWritable
SCG58 | TrProtocol.SerializerGenerator | Error | SerializeAsRequiresNumericTypes
