namespace RonatIa.Games.Domain.Errors;

/// <summary>Categoria de um erro esperado de negócio. A camada de API traduz cada categoria para um status HTTP.</summary>
public enum ErrorKind
{
    Validation,
    Unauthorized,
    Forbidden,
    NotFound,
    Conflict,
    PayloadTooLarge,
    RateLimited,
    Unavailable,
}
