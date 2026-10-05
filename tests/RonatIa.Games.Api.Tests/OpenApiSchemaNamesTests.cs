using System.Reflection;
using RonatIa.Games.Application;

namespace RonatIa.Games.Api.Tests;

public sealed class OpenApiSchemaNamesTests
{
    /// <summary>
    /// O gerador de OpenAPI nomeia cada esquema pelo nome simples do tipo. Dois tipos de contrato com o mesmo nome (em
    /// namespaces diferentes) colidem em silêncio: um esquema some do documento e as referências apontam para o errado.
    /// </summary>
    [Fact]
    public void Contract_types_have_unique_simple_names()
    {
        var assemblies = new[] { typeof(DependencyInjection).Assembly, typeof(Program).Assembly };

        var duplicates = assemblies
            .SelectMany(assembly => assembly.GetExportedTypes())
            .Where(type => type.IsClass || type.IsValueType)
            .Where(type => type.Name.EndsWith("Dto", StringComparison.Ordinal)
                || type.Name.EndsWith("Request", StringComparison.Ordinal)
                || type.Name.EndsWith("Response", StringComparison.Ordinal))
            .GroupBy(type => type.Name)
            .Where(group => group.Count() > 1)
            .Select(group => $"{group.Key}: {string.Join(" e ", group.Select(type => type.FullName))}")
            .ToList();

        Assert.True(duplicates.Count == 0, "Nomes de tipos de contrato repetidos (renomeie um deles): " + string.Join("; ", duplicates));
    }
}
