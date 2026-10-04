namespace RonatIa.Games.Application.Options;

public enum RegistrationMode
{
    /// <summary>Qualquer pessoa pode criar conta.</summary>
    Open,

    /// <summary>Válvula de escape contra abuso: ninguém novo se cadastra (quem já tem conta continua entrando).</summary>
    Closed,
}

/// <summary>Seção <c>Registration</c>.</summary>
public sealed class RegistrationOptions
{
    public const string SectionName = "Registration";

    public RegistrationMode Mode { get; set; } = RegistrationMode.Open;
}
