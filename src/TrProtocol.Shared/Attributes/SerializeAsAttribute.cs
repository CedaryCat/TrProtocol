namespace TrProtocol.Attributes;

/// <summary>
/// Instructs the source generator to serialize a primitive numeric member using another primitive numeric wire type.
/// </summary>
/// <remarks>
/// <para>
/// Generated code casts the member to <see cref="TargetType"/> when writing and casts the value back when reading.
/// Unsupported source or target types are rejected by the generator.
/// </para>
/// <para>
/// This controls a numeric wire representation without changing the declared in-memory numeric type. Exported members
/// whose public runtime type is wider than the wire field are a common use; non-exported models should normally declare
/// the wire type directly unless they have another explicit representation requirement.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property, AllowMultiple = false)]
public class SerializeAsAttribute : Attribute
{
    public Type TargetType;
    public SerializeAsAttribute(Type numberType) {
        TargetType = numberType;
    }
}
