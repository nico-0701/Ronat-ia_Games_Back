using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Options;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Api.Filters;

/// <summary>
/// Limita o corpo das ações que recebem foto de avatar: o tamanho máximo da foto (<see cref="AvatarOptions.MaxUploadBytes"/>)
/// mais uma folga para o envelope multipart. Responde 413 antes de ler qualquer byte quando o <c>Content-Length</c> já
/// passa do limite e, quando o tamanho não é conhecido (<c>Transfer-Encoding: chunked</c>), faz o servidor interromper a leitura no limite.
/// </summary>
/// <remarks>
/// Só o <c>[RequestSizeLimit]</c> não basta: o MVC trata a falha de leitura do formulário (<see cref="IOException"/>) como
/// erro de validação e responderia 400. Por isso este filtro também retira os provedores de valores de formulário
/// (o arquivo é lido pelo binder de <see cref="IFormFile"/>), e a exceção chega ao tratador global como 413.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class AvatarUploadLimitAttribute : Attribute, IResourceFilter
{
    /// <summary>Folga para o envelope multipart (fronteiras e cabeçalhos das partes).</summary>
    public const int MultipartOverheadBytes = 256 * 1024;

    public void OnResourceExecuting(ResourceExecutingContext context)
    {
        var http = context.HttpContext;
        var limit = (long)http.RequestServices.GetRequiredService<IOptions<AvatarOptions>>().Value.MaxUploadBytes + MultipartOverheadBytes;

        if (http.Request.ContentLength > limit)
        {
            throw TooLarge();
        }

        if (http.Features.Get<IHttpMaxRequestBodySizeFeature>() is { IsReadOnly: false } feature)
        {
            feature.MaxRequestBodySize = limit;
        }

        context.ValueProviderFactories.RemoveType<FormValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<FormFileValueProviderFactory>();
        context.ValueProviderFactories.RemoveType<JQueryFormValueProviderFactory>();
    }

    public void OnResourceExecuted(ResourceExecutedContext context)
    {
    }

    private static AppException TooLarge() => AppException.PayloadTooLarge(
        "request.too_large",
        "O envio é grande demais.",
        new Dictionary<string, string[]> { ["file"] = ["Escolha uma foto menor."] });
}
