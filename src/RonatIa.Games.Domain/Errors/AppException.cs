namespace RonatIa.Games.Domain.Errors;

/// <summary>
/// Erro esperado de negócio. Carrega um <see cref="Code"/> estável (ex.: <c>group.not_found</c>), que o cliente
/// usa para decidir o que fazer ou traduzir, e uma mensagem em português para exibição.
/// </summary>
public class AppException : Exception
{
    public AppException(ErrorKind kind, string code, string message, IReadOnlyDictionary<string, string[]>? errors = null)
        : base(message)
    {
        Kind = kind;
        Code = code;
        Errors = errors;
    }

    public ErrorKind Kind { get; }

    /// <summary>Identificador estável do erro, no formato <c>area.motivo</c>.</summary>
    public string Code { get; }

    /// <summary>Erros por campo (apenas para <see cref="ErrorKind.Validation"/>).</summary>
    public IReadOnlyDictionary<string, string[]>? Errors { get; }

    public static AppException Validation(string code, string message, IReadOnlyDictionary<string, string[]>? errors = null) =>
        new(ErrorKind.Validation, code, message, errors);

    public static AppException Unauthorized(string code, string message) => new(ErrorKind.Unauthorized, code, message);

    public static AppException Forbidden(string code, string message) => new(ErrorKind.Forbidden, code, message);

    public static AppException NotFound(string code, string message) => new(ErrorKind.NotFound, code, message);

    public static AppException Conflict(string code, string message) => new(ErrorKind.Conflict, code, message);

    public static AppException RateLimited(string code, string message) => new(ErrorKind.RateLimited, code, message);

    public static AppException Unavailable(string code, string message) => new(ErrorKind.Unavailable, code, message);
}
