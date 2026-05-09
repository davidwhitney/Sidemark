using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Sidemark.Internal;

internal static class SyntaxExtensions
{
    /// True for `[assembly: ...]` lists. Compare against `[module: ...]`, type-targeted, etc.
    public static bool IsAssemblyTarget(this AttributeListSyntax attrList) =>
        attrList.Target?.Identifier.IsKind(SyntaxKind.AssemblyKeyword) == true;

    /// Matches the syntactic attribute name (with or without `Attribute` suffix, with or without
    /// namespace qualification) against the supplied attribute type name.
    public static bool MatchesType(this AttributeSyntax attribute, string attributeTypeName) =>
        AttributeNameMatching.Matches(attribute.Name.ToString(), attributeTypeName);

    /// Enumerate all `[assembly: ...]` attributes anywhere under this root.
    public static IEnumerable<AttributeSyntax> AssemblyAttributes(this SyntaxNode root)
    {
        foreach (var attrList in root.DescendantNodes().OfType<AttributeListSyntax>())
        {
            if (!attrList.IsAssemblyTarget()) continue;
            foreach (var attr in attrList.Attributes)
            {
                yield return attr;
            }
        }
    }

    /// Return the segment after the last `.` in a dotted name (`A.B.C` → `C`). Used to strip
    /// namespace qualification from syntactic attribute and type names.
    public static string LastSegment(this string dottedName) =>
        dottedName.Substring(dottedName.LastIndexOf('.') + 1);
}
