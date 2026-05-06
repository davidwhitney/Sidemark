using Microsoft.CodeAnalysis;

namespace Sidemark.Analyzer;

internal static class Diagnostics
{
    private const string Category = "Sidemark";

    public static readonly DiagnosticDescriptor TagOnNonLocalDeclaration = new(
        id: "SDM001",
        title: "//? tag directive must be attached to a local variable declaration",
        messageFormat: "The //? tag directive must be attached to a local variable declaration; it has no effect here",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Tag directives derive the tag value from a local variable; they cannot be attached to other statements.");

    public static readonly DiagnosticDescriptor EventDirectiveMissingName = new(
        id: "SDM002",
        title: "//! event directive is missing an event name",
        messageFormat: "The //! event directive is missing a name; an event name is required",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "When //! is attached to a body statement (rather than a method signature), it adds an ActivityEvent and requires a name.");

    public static readonly DiagnosticDescriptor DirectiveOnUnsupportedMember = new(
        id: "SDM003",
        title: "Directive on a member that the rewriter does not process",
        messageFormat: "Sidemark only instruments methods and local functions with block bodies; the directive on this {0} will be ignored",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Constructors, property accessors, operators, and expression-bodied methods are not instrumented by the rewriter, so directives placed on their signatures have no effect.");

    public static readonly DiagnosticDescriptor CompoundMarkerOffSignature = new(
        id: "SDM004",
        title: "//?! compound marker is only meaningful on a method signature",
        messageFormat: "The //?! compound marker only does anything on a method or local-function signature; it has no effect here",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "//?! creates an activity and emits an entry event; outside a method/local-function signature there is nothing for it to wrap.");

    public static readonly DiagnosticDescriptor DuplicateTagKey = new(
        id: "SDM005",
        title: "Duplicate tag key within the same method",
        messageFormat: "The tag key '{0}' is set more than once in this method; later SetTag calls will overwrite earlier ones on the same activity",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Activity.SetTag(key, value) is last-write-wins. When the same key is emitted twice from a single method, the earlier value is silently lost.");

    public static readonly DiagnosticDescriptor CatchAnnotationHasIgnoredPayload = new(
        id: "SDM006",
        title: "catch //? annotation does not take a payload",
        messageFormat: "The //? annotation on a catch clause does not accept a payload; the text after //? is ignored",
        category: Category,
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "On a catch clause, //? always emits SetStatus(Error, ex.Message). It has no name override, so any payload is silently discarded.");
}
