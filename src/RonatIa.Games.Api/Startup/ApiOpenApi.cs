using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace RonatIa.Games.Api.Startup;

public static class ApiOpenApi
{
    /// <summary>Documento OpenAPI 3.1 gerado pelo próprio ASP.NET Core; é o contrato consumido pelo Front.</summary>
    public static IServiceCollection AddApiOpenApi(this IServiceCollection services)
    {
        services.AddOpenApi("v1", options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Ronat-ia Games API",
                    Version = "v1",
                    Description = "API da plataforma de jogos Ronat-ia Games. Erros seguem ProblemDetails (RFC 9457) com um `code` estável.",
                };

                // O contrato não depende do host em que o documento foi gerado.
                document.Servers = new List<OpenApiServer>();

                // Por padrão o MVC anuncia text/plain e text/json além de application/json; a API só fala JSON.
                var operations = document.Paths.Values
                    .Where(path => path.Operations is not null)
                    .SelectMany(path => path.Operations!.Values);

                foreach (var operation in operations.Where(operation => operation.Responses is not null))
                {
                    foreach (var response in operation.Responses!.Values)
                    {
                        response.Content?.Remove("text/plain");
                        response.Content?.Remove("text/json");
                    }
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
