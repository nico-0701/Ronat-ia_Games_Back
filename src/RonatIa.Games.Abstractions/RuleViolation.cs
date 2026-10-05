namespace RonatIa.Games.Abstractions;

/// <summary>Como a plataforma responde a uma ação recusada pelo módulo.</summary>
public enum RuleViolationKind
{
    /// <summary>A ação em si está mal formada (campo ausente, valor fora da faixa). Vira 400.</summary>
    InvalidAction,

    /// <summary>Quem enviou não pode fazer isso (não é a vez, não é o mímico). Vira 403.</summary>
    NotAllowed,

    /// <summary>O estado atual não permite (outra fase, prazo vencido, já acabou). Vira 409.</summary>
    InvalidState,
}

/// <summary>
/// Lançada pelo módulo quando uma ação não é válida. O estado não muda. O <see cref="Code"/> é estável e segue o formato
/// <c>jogo.motivo</c> (ex.: <c>mimica.not_performer</c>), para o cliente decidir o que mostrar.
/// </summary>
public sealed class RuleViolation(RuleViolationKind kind, string code, string message) : Exception(message)
{
    public RuleViolationKind Kind { get; } = kind;

    public string Code { get; } = code;

    public static RuleViolation Invalid(string code, string message) => new(RuleViolationKind.InvalidAction, code, message);

    public static RuleViolation NotAllowed(string code, string message) => new(RuleViolationKind.NotAllowed, code, message);

    public static RuleViolation WrongState(string code, string message) => new(RuleViolationKind.InvalidState, code, message);
}
