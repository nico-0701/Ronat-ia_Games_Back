namespace RonatIa.Games.Api.Startup;

/// <summary>
/// Formato de todo erro da API: <c>ProblemDetails</c> (RFC 9457) mais os campos próprios abaixo.
/// Este tipo existe só para descrever o contrato (OpenAPI); em tempo de execução o corpo é gerado por <c>ProblemDetails</c>.
/// </summary>
/// <param name="Type">URN do erro, no formato <c>urn:ronat-ia:error:{code}</c> (quando o erro vem de uma regra de negócio).</param>
/// <param name="Title">Título curto em português.</param>
/// <param name="Status">Status HTTP.</param>
/// <param name="Detail">Mensagem em português, própria para exibir à pessoa.</param>
/// <param name="Instance">Caminho da requisição.</param>
/// <param name="Code">Identificador estável do erro (ex.: <c>group.not_found</c>, <c>auth.user_not_found</c>); use-o para decidir o que fazer, não o texto.</param>
/// <param name="TraceId">Identificador da requisição, para suporte.</param>
/// <param name="Errors">Erros por campo (somente em validações).</param>
public sealed record ApiProblem(
    string? Type,
    string? Title,
    int Status,
    string? Detail,
    string? Instance,
    string Code,
    string TraceId,
    IDictionary<string, string[]>? Errors);
