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
            // Erro gerado pelo framework (401, 404, 415, validação de modelo...): completa com o padrão da API, para que todo erro
            // tenha o mesmo formato (type urn:ronat-ia:error:{code}, code estável e detail em português).
            var code = problem is ValidationProblemDetails ? "validation.failed" : CodeFor(problem.Status);
            problem.Extensions["code"] = code;
            problem.Type = $"urn:ronat-ia:error:{code}";
            problem.Title = problem is ValidationProblemDetails ? "Requisição inválida" : TitleFor(problem.Status);
            problem.Detail ??= problem is ValidationProblemDetails ? "Alguns dados estão inválidos. Confira e tente de novo." : DetailFor(problem.Status);
        }
    }

    public static string DetailFor(int? status) => status switch
    {
        400 => "A requisição não pôde ser entendida. Confira os dados e tente de novo.",
        401 => "Você precisa entrar para continuar.",
        403 => "Você não tem permissão para fazer isto.",
        404 => "Não encontramos o que você procurou.",
        405 => "Esta operação não existe neste endereço.",
        409 => "A operação conflita com o estado atual. Atualize e tente de novo.",
        413 => "O envio é grande demais.",
        415 => "O tipo de conteúdo enviado não é aceito.",
        429 => "Foram feitas requisições demais em pouco tempo. Aguarde um instante e tente de novo.",
        >= 500 => "Algo deu errado do nosso lado. Tente de novo em instantes.",
        _ => "Não foi possível concluir a operação.",
    };

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
