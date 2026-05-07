namespace Sidemark;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableSidemarkAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class SidemarkAttribute(Type configurationType) : Attribute
{
    public Type ConfigurationType { get; } = configurationType;
}

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class SidemarkActivitySourceAttribute(Type containingType, string memberName) : Attribute
{
    public Type ContainingType { get; } = containingType;
    public string MemberName { get; } = memberName;
}
