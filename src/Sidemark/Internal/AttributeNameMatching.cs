using System;

namespace Sidemark.Internal;

internal static class AttributeNameMatching
{
    private const string AttributeSuffix = "Attribute";

    /// Match a syntactic attribute name (possibly namespace-qualified, possibly with the "Attribute" suffix omitted)
    /// against the supplied attribute type name.
    public static bool Matches(string syntaxName, string attributeTypeName)
    {
        var lastSegment = syntaxName.LastSegment();
        var shortName = attributeTypeName.EndsWith(AttributeSuffix, StringComparison.Ordinal)
            ? attributeTypeName.Substring(0, attributeTypeName.Length - AttributeSuffix.Length)
            : attributeTypeName;
        return lastSegment == shortName || lastSegment == attributeTypeName;
    }
}
