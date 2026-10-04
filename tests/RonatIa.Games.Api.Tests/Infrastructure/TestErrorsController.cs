using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using RonatIa.Games.Domain.Errors;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>Endpoints que só existem nos testes, para exercitar o tratamento de erros.</summary>
[ApiController]
[AllowAnonymous]
[ApiExplorerSettings(IgnoreApi = true)]
[Route("test/errors")]
public sealed class TestErrorsController : ControllerBase
{
    [HttpGet("not-found")]
    public IActionResult NotFoundCase() => throw AppException.NotFound("test.not_found", "Recurso de teste não encontrado.");

    [HttpGet("conflict")]
    public IActionResult ConflictCase() => throw AppException.Conflict("test.conflict", "Conflito de teste.");

    [HttpGet("forbidden")]
    public IActionResult ForbiddenCase() => throw AppException.Forbidden("test.forbidden", "Sem permissão de teste.");

    [HttpGet("validation")]
    public IActionResult ValidationCase() => throw AppException.Validation(
        "test.invalid",
        "Dados inválidos.",
        new Dictionary<string, string[]> { ["name"] = ["Nome muito curto."] });

    [HttpGet("boom")]
    public IActionResult Boom() => throw new InvalidOperationException("segredo-que-nao-pode-vazar");

    [HttpPost("validate")]
    public IActionResult Validate([FromBody] ValidateBody body) => Ok(body);
}

public sealed class ValidateBody
{
    [Required]
    [MinLength(3)]
    public string? Name { get; set; }
}
