using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace RonatIa.Games.Api.Startup.Errors;

/// <summary>Padroniza todas as respostas de erro: <c>code</c> estável, título em português e <c>traceId</c>.</summary>
internal static class ProblemDetailsDefaults
{
    public static void Customize(ProblemDetailsContext context)
    {
        var problem = context.ProblemDetails;

        problem.Extensions["traceId"] = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;

        if (!problem.Extensions.ContainsKey("code"))
        {
            problem.Extensions["code"] = problem is ValidationProblemDetails ? "validation.failed" : CodeFor(problem.Status);
            problem.Title = problem is ValidationProblemDetails ? "Requisição inválida" : TitleFor(problem.Status);
        }
    }

    public static string CodeFor(int? status) => status switch
    {
        400 => "http.bad_request",
        401 => "auth.unauthorized",
        403 => "auth.forbidden",
        404 => "http.not_found",
        405 => "http.method_not_allowed",
        409 => "http.conflict",
        413 => "request.too_large",
        415 => "http.unsupported_media_type",
        429 => "rate_limit.exceeded",
        >= 500 => "server.error",
        _ => "http.error",
    };

    public static string TitleFor(int? status) => status switch
    {
        400 => "Requisição inválida",
        401 => "Não autenticado",
        403 => "Acesso negado",
        404 => "Não encontrado",
        405 => "Método não permitido",
        409 => "Conflito",
        413 => "Conteúdo grande demais",
        415 => "Tipo de conteúdo não suportado",
        429 => "Muitas requisições",
        >= 500 => "Erro interno",
        _ => "Erro",
    };
}
