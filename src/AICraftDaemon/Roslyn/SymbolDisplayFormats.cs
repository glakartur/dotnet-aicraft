using System.Text;
using Microsoft.CodeAnalysis;

namespace DotnetAICraft.Roslyn;

/// <summary>
/// Custom <see cref="SymbolDisplayFormat"/>s shared by the symbol-inspection commands
/// (<c>describe</c>, <c>outline</c>, <c>source</c>). No predefined format emits accessibility +
/// modifiers together, nor a type's base/interface list, nor the <c>async</c> keyword (which is
/// compiler-only and absent from metadata), so these are assembled here. See plan decision D4.
/// </summary>
public static class SymbolDisplayFormats
{
    /// <summary>
    /// Full member signature for the <c>describe</c> card: accessibility + modifiers + return type +
    /// name + generics/constraints + parameters (with names, defaults, <c>params/ref/in/out</c>) +
    /// constant value. NRT annotations and special type keywords (<c>int</c> over <c>System.Int32</c>)
    /// are on. Does <b>not</b> include <c>async</c> — use <see cref="FormatMemberSignature"/>.
    /// </summary>
    public static readonly SymbolDisplayFormat MemberSignatureFormat = new(
        memberOptions:
            SymbolDisplayMemberOptions.IncludeType |
            SymbolDisplayMemberOptions.IncludeModifiers |
            SymbolDisplayMemberOptions.IncludeAccessibility |
            SymbolDisplayMemberOptions.IncludeExplicitInterface |
            SymbolDisplayMemberOptions.IncludeParameters |
            SymbolDisplayMemberOptions.IncludeConstantValue |
            SymbolDisplayMemberOptions.IncludeRef,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeParamsRefOut |
            SymbolDisplayParameterOptions.IncludeDefaultValue |
            SymbolDisplayParameterOptions.IncludeExtensionThis,
        genericsOptions:
            SymbolDisplayGenericsOptions.IncludeTypeParameters |
            SymbolDisplayGenericsOptions.IncludeTypeConstraints |
            SymbolDisplayGenericsOptions.IncludeVariance,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);

    /// <summary>
    /// Compact member line for <c>outline</c>: accessibility + modifiers + type + name + parameters,
    /// no parameter default values, no property accessor list (<c>{ get; set; }</c>). Special type
    /// keywords on. Used via <see cref="FormatMemberSignature"/> for <c>async</c> prefixing.
    /// </summary>
    public static readonly SymbolDisplayFormat OutlineMemberFormat = new(
        memberOptions:
            SymbolDisplayMemberOptions.IncludeType |
            SymbolDisplayMemberOptions.IncludeModifiers |
            SymbolDisplayMemberOptions.IncludeAccessibility |
            SymbolDisplayMemberOptions.IncludeExplicitInterface |
            SymbolDisplayMemberOptions.IncludeParameters |
            SymbolDisplayMemberOptions.IncludeConstantValue |
            SymbolDisplayMemberOptions.IncludeRef,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeParamsRefOut |
            SymbolDisplayParameterOptions.IncludeExtensionThis,
        genericsOptions:
            SymbolDisplayGenericsOptions.IncludeTypeParameters |
            SymbolDisplayGenericsOptions.IncludeVariance,
        propertyStyle: SymbolDisplayPropertyStyle.NameOnly,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier |
            SymbolDisplayMiscellaneousOptions.UseErrorTypeSymbolName);

    /// <summary>
    /// Name + type parameters + constraints for a type, with no keyword/accessibility/modifiers
    /// (those are assembled by <see cref="FormatTypeHeader"/>) and no namespace qualification.
    /// e.g. <c>Foo&lt;T&gt; where T : struct</c>.
    /// </summary>
    public static readonly SymbolDisplayFormat TypeHeaderFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameOnly,
        genericsOptions:
            SymbolDisplayGenericsOptions.IncludeTypeParameters |
            SymbolDisplayGenericsOptions.IncludeTypeConstraints |
            SymbolDisplayGenericsOptions.IncludeVariance,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.EscapeKeywordIdentifiers |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    /// <summary>Minimal qualified type name for base/interface lists — e.g. <c>List&lt;int&gt;</c>, <c>IBaz</c>.</summary>
    private static readonly SymbolDisplayFormat BaseTypeNameFormat = SymbolDisplayFormat.MinimallyQualifiedFormat;

    /// <summary>
    /// Renders a member's signature with <see cref="MemberSignatureFormat"/> (or a caller-supplied
    /// format), prepending <c>async</c> for an async method — <c>async</c> is compiler-only and not
    /// reproduced by any display format. See plan decision D4.
    /// </summary>
    public static string FormatMemberSignature(ISymbol symbol, SymbolDisplayFormat? format = null)
    {
        var signature = symbol.ToDisplayString(format ?? MemberSignatureFormat);
        return symbol is IMethodSymbol { IsAsync: true }
            ? "async " + signature
            : signature;
    }

    /// <summary>
    /// Assembles a full type header — <c>public sealed class Foo&lt;T&gt; : Bar, IBaz where T : struct</c> —
    /// from accessibility + modifiers + keyword + <see cref="TypeHeaderFormat"/> name, then appends the
    /// manually-built base/interface list (skipping <c>System.Object</c>/<c>System.ValueType</c>). See D4.
    /// </summary>
    public static string FormatTypeHeader(INamedTypeSymbol type)
    {
        var sb = new StringBuilder();

        var accessibility = AccessibilityKeyword(type.DeclaredAccessibility);
        if (accessibility.Length > 0)
            sb.Append(accessibility).Append(' ');

        foreach (var modifier in TypeModifiers(type))
            sb.Append(modifier).Append(' ');

        sb.Append(KindKeyword(type));

        var nameWithGenerics = type.ToDisplayString(TypeHeaderFormat);
        // TypeHeaderFormat already includes any "where" clause; split it off so the base list slots
        // in before the constraints: "class Foo<T> : Bar where T : struct".
        var whereIndex = nameWithGenerics.IndexOf(" where ", StringComparison.Ordinal);
        var namePart = whereIndex < 0 ? nameWithGenerics : nameWithGenerics[..whereIndex];
        var constraintPart = whereIndex < 0 ? null : nameWithGenerics[whereIndex..];

        sb.Append(' ').Append(namePart);

        var baseList = BaseAndInterfaceList(type);
        if (baseList.Count > 0)
            sb.Append(" : ").Append(string.Join(", ", baseList));

        if (constraintPart is not null)
            sb.Append(constraintPart);

        return sb.ToString();
    }

    /// <summary>
    /// Renders a delegate type as its invoke signature — e.g. <c>public delegate int Fourth(string s)</c>.
    /// </summary>
    public static string FormatDelegateSignature(INamedTypeSymbol delegateType)
    {
        var invoke = delegateType.DelegateInvokeMethod;

        var sb = new StringBuilder();
        var accessibility = AccessibilityKeyword(delegateType.DeclaredAccessibility);
        if (accessibility.Length > 0)
            sb.Append(accessibility).Append(' ');
        sb.Append("delegate ");
        sb.Append(invoke is null ? "void" : invoke.ReturnType.ToDisplayString(BaseTypeNameFormat));
        sb.Append(' ');

        var nameWithGenerics = delegateType.ToDisplayString(TypeHeaderFormat);
        var whereIndex = nameWithGenerics.IndexOf(" where ", StringComparison.Ordinal);
        var namePart = whereIndex < 0 ? nameWithGenerics : nameWithGenerics[..whereIndex];
        var constraintPart = whereIndex < 0 ? null : nameWithGenerics[whereIndex..];

        sb.Append(namePart).Append('(');
        if (invoke is not null)
            sb.Append(string.Join(", ", invoke.Parameters.Select(p => p.ToDisplayString(MemberSignatureFormat))));
        sb.Append(')');

        if (constraintPart is not null)
            sb.Append(constraintPart);

        return sb.ToString();
    }

    private static IReadOnlyList<string> BaseAndInterfaceList(INamedTypeSymbol type)
    {
        var entries = new List<string>();

        if (type.TypeKind == TypeKind.Class &&
            type.BaseType is { } baseType &&
            baseType.SpecialType is not SpecialType.System_Object and not SpecialType.System_ValueType)
        {
            entries.Add(baseType.ToDisplayString(BaseTypeNameFormat));
        }

        foreach (var iface in type.Interfaces)
            entries.Add(iface.ToDisplayString(BaseTypeNameFormat));

        return entries;
    }

    private static IEnumerable<string> TypeModifiers(INamedTypeSymbol type)
    {
        // Static classes are sealed+abstract in metadata; surface only "static".
        if (type.IsStatic)
        {
            yield return "static";
            yield break;
        }

        if (type.IsAbstract && type.TypeKind == TypeKind.Class)
            yield return "abstract";
        if (type.IsSealed && type.TypeKind == TypeKind.Class && !type.IsRecord)
            yield return "sealed";
        if (type.IsReadOnly)
            yield return "readonly";
        if (type.IsRefLikeType)
            yield return "ref";
    }

    private static string KindKeyword(INamedTypeSymbol type) => type.TypeKind switch
    {
        TypeKind.Class when type.IsRecord => "record",
        TypeKind.Class => "class",
        TypeKind.Struct when type.IsRecord => "record struct",
        TypeKind.Struct => "struct",
        TypeKind.Interface => "interface",
        TypeKind.Enum => "enum",
        TypeKind.Delegate => "delegate",
        _ => type.TypeKind.ToString().ToLowerInvariant()
    };

    private static string AccessibilityKeyword(Accessibility accessibility) => accessibility switch
    {
        Accessibility.Public => "public",
        Accessibility.Internal => "internal",
        Accessibility.Private => "private",
        Accessibility.Protected => "protected",
        Accessibility.ProtectedOrInternal => "protected internal",
        Accessibility.ProtectedAndInternal => "private protected",
        _ => string.Empty
    };
}
