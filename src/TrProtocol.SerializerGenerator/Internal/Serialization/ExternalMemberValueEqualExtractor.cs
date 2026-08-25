using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using TrProtocol.Attributes;
using TrProtocol.SerializerGenerator.Internal.Diagnostics;
using TrProtocol.SerializerGenerator.Internal.Extensions;
using TrProtocol.SerializerGenerator.Internal.Models;

namespace TrProtocol.SerializerGenerator.Internal.Serialization;

/// <summary>
/// Builds dynamic boolean bindings declared with <see cref="ExternalMemberValueEqualAttribute"/>.
/// </summary>
public static class ExternalMemberValueEqualExtractor
{
    public static List<(string memberName, string memberValue)> Extract(
        SerializationExpandContext member,
        ITypeSymbol memberTypeSymbol,
        INamedTypeSymbol ownerTypeSymbol,
        ISymbol ownerMemberSymbol,
        string? ownerAccess,
        IEnumerable<string> reservedTargets) {
        var attributes = ownerMemberSymbol.GetAttributes()
            .Where(a => a.AttributeClass?.Name == nameof(ExternalMemberValueEqualAttribute))
            .ToArray();
        if (attributes.Length == 0) {
            return [];
        }

        var nestedType = UnwrapArray(memberTypeSymbol) as INamedTypeSymbol;
        var assignedTargets = new HashSet<string>(reservedTargets, StringComparer.Ordinal);
        List<(string memberName, string memberValue)> values = [];

        foreach (var attribute in attributes) {
            var location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation()
                ?? member.MemberDeclaration.GetLocation();
            if (attribute.ConstructorArguments.Length != 3
                || attribute.ConstructorArguments[0].Value is not string targetMember
                || attribute.ConstructorArguments[1].Value is not string sourceMember
                || string.IsNullOrWhiteSpace(targetMember)
                || string.IsNullOrWhiteSpace(sourceMember)
                || nestedType is null
                || !assignedTargets.Add(targetMember)) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ExternalMemberValueEqualArgumentsInvalid,
                        location,
                        member.MemberName));
            }

            var sourceSymbol = FindInstanceMember(ownerTypeSymbol, sourceMember);
            if (sourceSymbol is null) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ExternalMemberValueEqualSourceNotFound,
                        location,
                        member.MemberName,
                        sourceMember,
                        ownerTypeSymbol.Name));
            }

            var targetSymbol = FindInstanceMember(nestedType, targetMember);
            if (targetSymbol is null) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ExternalMemberValueEqualTargetNotFound,
                        location,
                        member.MemberName,
                        targetMember,
                        nestedType.Name));
            }

            if (!targetSymbol.GetAttributes().Any(a => a.AttributeClass?.Name == nameof(ExternalMemberAttribute))) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ExternalMemberValueEqualTargetMustBeExternal,
                        location,
                        targetMember,
                        nestedType.Name));
            }

            var targetType = GetMemberType(targetSymbol)!;
            if (targetType.SpecialType != SpecialType.System_Boolean) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ExternalMemberValueEqualTargetMustBeBoolean,
                        location,
                        targetMember,
                        nestedType.Name,
                        targetType.ToDisplayString()));
            }

            if (!IsWritableFromGeneratedCode(targetSymbol)) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ExternalMemberValueEqualTargetNotWritable,
                        location,
                        targetMember,
                        nestedType.Name));
            }

            if (!SourcePrecedesTarget(ownerTypeSymbol, sourceMember, member.MemberName)) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ExternalMemberValueEqualSourceMustPrecedeTarget,
                        location,
                        sourceMember,
                        member.MemberName,
                        ownerTypeSymbol.Name));
            }

            var sourceType = GetMemberType(sourceSymbol)!;
            var expected = attribute.ConstructorArguments[2];
            if (expected.Type is null || !SymbolEqualityComparer.Default.Equals(sourceType, expected.Type)) {
                throw new DiagnosticException(
                    Diagnostic.Create(
                        DiagnosticDescriptors.ExternalMemberValueEqualExpectedTypeMismatch,
                        location,
                        sourceMember,
                        sourceType.ToDisplayString(),
                        expected.Type?.ToDisplayString() ?? "null"));
            }

            var sourceAccess = string.IsNullOrEmpty(ownerAccess)
                ? sourceMember
                : $"{ownerAccess}.{sourceMember}";
            values.Add((targetMember, $"{sourceAccess} == {FormatTypedConstant(expected, sourceType)}"));
        }

        return values;
    }

    private static ITypeSymbol UnwrapArray(ITypeSymbol type) {
        return type is IArrayTypeSymbol array ? array.ElementType : type;
    }

    private static ISymbol? FindInstanceMember(INamedTypeSymbol type, string name) {
        for (INamedTypeSymbol? current = type; current is not null; current = current.BaseType) {
            var candidate = current.GetMembers(name)
                .FirstOrDefault(m => !m.IsStatic && m is IFieldSymbol or IPropertySymbol);
            if (candidate is not null) {
                return candidate;
            }
        }
        return null;
    }

    private static ITypeSymbol? GetMemberType(ISymbol member) {
        return member switch {
            IFieldSymbol field => field.Type,
            IPropertySymbol property => property.Type,
            _ => null,
        };
    }

    private static bool IsWritableFromGeneratedCode(ISymbol member) {
        return member switch {
            IFieldSymbol field => !field.IsConst
                && !field.IsReadOnly
                && field.DeclaredAccessibility == Accessibility.Public,
            IPropertySymbol property => property.SetMethod is {
                DeclaredAccessibility: Accessibility.Public,
                IsInitOnly: false,
            },
            _ => false,
        };
    }

    private static bool SourcePrecedesTarget(
        INamedTypeSymbol ownerType,
        string sourceMember,
        string targetMember) {
        var orderedMembers = ownerType.DeclaringSyntaxReferences
            .Select(reference => reference.GetSyntax())
            .OfType<TypeDeclarationSyntax>()
            .SelectMany(type => type.Members.Select(declaration => new {
                Declaration = declaration,
                Location = declaration.GetLocation(),
            }))
            .OrderBy(item => item.Location.SourceTree?.FilePath ?? "", StringComparer.Ordinal)
            .ThenBy(item => item.Location.SourceSpan.Start)
            .SelectMany(item => GetSerializableMemberNames(item.Declaration))
            .ToArray();

        var sourceIndex = Array.IndexOf(orderedMembers, sourceMember);
        var targetIndex = Array.IndexOf(orderedMembers, targetMember);
        return sourceIndex >= 0 && targetIndex > sourceIndex;
    }

    private static IEnumerable<string> GetSerializableMemberNames(MemberDeclarationSyntax declaration) {
        var forceInclude = declaration.AttributeMatch<IncludeSerializeAttribute>();
        if (declaration.AttributeMatch<IgnoreSerializeAttribute>()) {
            yield break;
        }

        if (declaration is FieldDeclarationSyntax field) {
            if (field.Modifiers.Any(m => m.IsKind(SyntaxKind.ConstKeyword))
                || (!forceInclude && !field.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))) {
                yield break;
            }
            foreach (var variable in field.Declaration.Variables) {
                yield return variable.Identifier.ValueText;
            }
            yield break;
        }

        if (declaration is not PropertyDeclarationSyntax property
            || property.AccessorList is null
            || (!forceInclude && !property.Modifiers.Any(m => m.IsKind(SyntaxKind.PublicKeyword)))) {
            yield break;
        }

        foreach (var accessorName in new[] { "get", "set" }) {
            var accessor = property.AccessorList.Accessors
                .FirstOrDefault(a => a.Keyword.ValueText == accessorName);
            if (accessor is null
                || (!forceInclude && accessor.Modifiers.Any(m => m.IsKind(SyntaxKind.PrivateKeyword)
                    || m.IsKind(SyntaxKind.ProtectedKeyword)))) {
                yield break;
            }
        }
        yield return property.Identifier.ValueText;
    }

    private static string FormatTypedConstant(TypedConstant constant, ITypeSymbol sourceType) {
        if (constant.Kind == TypedConstantKind.Enum && sourceType is INamedTypeSymbol enumType) {
            var matchingMember = enumType.GetMembers()
                .OfType<IFieldSymbol>()
                .FirstOrDefault(field => field.HasConstantValue
                    && Equals(field.ConstantValue, constant.Value));
            if (matchingMember is not null) {
                return $"{enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}.{matchingMember.Name}";
            }
        }

        if (constant.IsNull) {
            return "null";
        }

        var literal = SymbolDisplay.FormatPrimitive(
            constant.Value!,
            quoteStrings: true,
            useHexadecimalNumbers: false);
        return $"({sourceType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)}){literal}";
    }
}
