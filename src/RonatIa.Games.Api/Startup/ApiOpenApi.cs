using Microsoft.AspNetCore.Authorization;
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
            options.AddDocumentTransformer(async (document, context, cancellationToken) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = "Ronat-ia Games API",
                    Version = "v1",
                    Description = "API da plataforma de jogos Ronat-ia Games. Erros seguem ProblemDetails (RFC 9457) com um `code` estável. " +
                        "O login é só pelo telefone (sem SMS e sem senha); as demais chamadas usam `Authorization: Bearer <accessToken>`.",
                };

                // O contrato não depende do host em que o documento foi gerado.
                document.Servers = new List<OpenApiServer>();

                document.Components ??= new OpenApiComponents();
                document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
                document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
                {
                    Type = SecuritySchemeType.Http,
                    Scheme = "bearer",
                    BearerFormat = "JWT",
                    Description = "Access token devolvido por /api/v1/auth/login, /register ou /refresh.",
                };

                // Por padrão o MVC anuncia text/plain e text/json além de application/json; a API só fala JSON.
                // Todo erro é um ProblemDetails (resposta "default").
                var problemSchema = await context.GetOrCreateSchemaAsync(typeof(ApiProblem), null, cancellationToken);
                document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
                document.Components.Schemas["ApiProblem"] = problemSchema;
                var problemReference = new OpenApiSchemaReference("ApiProblem", document);
                var operations = document.Paths.Values
                    .Where(path => path.Operations is not null)
                    .SelectMany(path => path.Operations!.Values);

                foreach (var operation in operations)
                {
                    operation.Responses ??= new OpenApiResponses();
                    foreach (var response in operation.Responses.Values)
                    {
                        response.Content?.Remove("text/plain");
                        response.Content?.Remove("text/json");
                    }

                    operation.Responses["default"] = new OpenApiResponse
                    {
                        Description = "Erro. O corpo é um ProblemDetails com `code` (estável), `detail`, `traceId` e, em validações, `errors` por campo.",
                        Content = new Dictionary<string, OpenApiMediaType>
                        {
                            ["application/problem+json"] = new OpenApiMediaType { Schema = problemReference },
                        },
                    };
                }
            });

            options.AddOperationTransformer((operation, context, _) =>
            {
                var metadata = context.Description.ActionDescriptor.EndpointMetadata;
                var requiresAuthentication = !metadata.OfType<IAllowAnonymous>().Any();

                if (requiresAuthentication)
                {
                    operation.Security ??= new List<OpenApiSecurityRequirement>();
                    operation.Security.Add(new OpenApiSecurityRequirement
                    {
                        [new OpenApiSecuritySchemeReference("Bearer", context.Document)] = [],
                    });
                }

                return Task.CompletedTask;
            });
        });

        return services;
    }
}
