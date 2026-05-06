namespace Sidemark;

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class DisableSidemarkAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false, Inherited = false)]
public sealed class SidemarkAttribute : Attribute
{
    public SidemarkAttribute(Type configurationType)
    {
        ConfigurationType = configurationType;
    }

    public Type ConfigurationType { get; }
}

[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = false)]
public sealed class SidemarkActivitySourceAttribute : Attribute
{
    public SidemarkActivitySourceAttribute(Type containingType, string memberName)
    {
        ContainingType = containingType;
        MemberName = memberName;
    }

    public Type ContainingType { get; }

    public string MemberName { get; }
}
