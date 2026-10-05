using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Api.Startup.Errors;

/// <summary>
/// Traduz exceções em respostas <c>application/problem+json</c> (RFC 9457). Erros esperados de negócio
/// (<see cref="AppException"/>) viram 4xx com seu <c>code</c>; qualquer outra coisa vira 500 genérico,
/// sem vazar mensagem nem pilha para o cliente.
/// </summary>
public sealed class GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger, IProblemDetailsService problemDetailsService)
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            // O cliente desistiu da requisição; não é erro do servidor.
            return true;
        }

        var problem = Map(exception);

        if (problem.Status >= 500)
        {
            logger.LogError(exception, "Erro não tratado em {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
        }
        else
        {
            logger.LogInformation("Requisição recusada: {Code} em {Method} {Path}", problem.Extensions["code"], httpContext.Request.Method, httpContext.Request.Path);
        }

        httpContext.Response.StatusCode = problem.Status ?? StatusCodes.Status500InternalServerError;

        var written = await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = problem,
            Exception = exception,
        });

        if (!written)
        {
            // O cliente não aceita JSON (Accept incomum): escreve mesmo assim, em problem+json.
            await httpContext.Response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json", cancellationToken);
        }

        return true;
    }

    private static ProblemDetails Map(Exception exception)
    {
        switch (exception)
        {
            case AppException app:
                {
                    var (status, title) = app.Kind switch
                    {
                        ErrorKind.Validation => (StatusCodes.Status400BadRequest, "Requisição inválida"),
                        ErrorKind.Unauthorized => (StatusCodes.Status401Unauthorized, "Não autenticado"),
                        ErrorKind.Forbidden => (StatusCodes.Status403Forbidden, "Acesso negado"),
                        ErrorKind.NotFound => (StatusCodes.Status404NotFound, "Não encontrado"),
                        ErrorKind.Conflict => (StatusCodes.Status409Conflict, "Conflito"),
                        ErrorKind.PayloadTooLarge => (StatusCodes.Status413PayloadTooLarge, ProblemDetailsDefaults.TitleFor(StatusCodes.Status413PayloadTooLarge)),
                        ErrorKind.RateLimited => (StatusCodes.Status429TooManyRequests, "Muitas requisições"),
                        ErrorKind.Unavailable => (StatusCodes.Status503ServiceUnavailable, "Serviço indisponível"),
                        _ => (StatusCodes.Status500InternalServerError, "Erro interno"),
                    };

                    var problem = Build(status, title, app.Message, app.Code);
                    if (app.Errors is { Count: > 0 })
                    {
                        problem.Extensions["errors"] = app.Errors;
                    }

                    return problem;
                }

            case BadHttpRequestException bad:
                {
                    var status = bad.StatusCode is >= 400 and < 500 ? bad.StatusCode : StatusCodes.Status400BadRequest;
                    var code = status == StatusCodes.Status413PayloadTooLarge ? "request.too_large" : "request.malformed";
                    var title = ProblemDetailsDefaults.TitleFor(status);
                    return Build(status, title, "A requisição está malformada ou é grande demais.", code);
                }

            default:
                return Build(
                    StatusCodes.Status500InternalServerError,
                    "Erro interno",
                    "Ocorreu um erro inesperado. Informe o traceId ao suporte, se precisar.",
                    "server.error");
        }
    }

    private static ProblemDetails Build(int status, string title, string detail, string code)
    {
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Detail = detail,
            Type = $"urn:ronat-ia:error:{code}",
        };
        problem.Extensions["code"] = code;
        return problem;
    }
}
