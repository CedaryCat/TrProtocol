namespace TrProtocol.Attributes;

/// <summary>
/// Supplies a nested boolean <see cref="ExternalMemberAttribute"/> from an equality comparison on
/// an earlier member of the declaring protocol type.
/// </summary>
/// <remarks>
/// For a member annotated with
/// <c>[ExternalMemberValueEqual(targetMember, sourceMember, expectedValue)]</c>, generated readers
/// pass <c>sourceMember == expectedValue</c> to the nested reader and generated writers assign that
/// result to <c>targetMember</c> immediately before writing the nested value.
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = true)]
public sealed class ExternalMemberValueEqualAttribute : Attribute
{
    public readonly string TargetMember;
    public readonly string SourceMember;
    public readonly object ExpectedValue;

    public ExternalMemberValueEqualAttribute(string targetMember, string sourceMember, object expectedValue) {
        TargetMember = targetMember;
        SourceMember = sourceMember;
        ExpectedValue = expectedValue;
    }
}
