using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using RonatIa.Games.Api.Filters;
using RonatIa.Games.Api.Startup;
using RonatIa.Games.Application.Groups;

namespace RonatIa.Games.Api.Controllers;

/// <summary>
/// Grupos de amigos. Quem cria recebe a "senha do grupo" para compartilhar; quem tem a senha entra. Todas as rotas exigem
/// login; quem não é membro de um grupo recebe 404 (o servidor não revela que o grupo existe).
/// </summary>
[ApiController]
[Route("api/v1/groups")]
public sealed class GroupsController(GroupService groups, GroupMemberService members) : ControllerBase
{
    /// <summary>Os grupos de que a pessoa participa, por ordem alfabética.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GroupSummaryDto>>> List(CancellationToken cancellationToken) =>
        Ok(await groups.ListAsync(User.RequireUserId(), cancellationToken));

    /// <summary>Cria um grupo. Quem cria é o dono e recebe a senha do grupo na resposta.</summary>
    [HttpPost]
    [EnableRateLimiting(RateLimitPolicies.GroupCreate)]
    [ProducesResponseType<GroupDetailDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CreateGroupRequest request, CancellationToken cancellationToken)
    {
        var group = await groups.CreateAsync(User.RequireUserId(), request, cancellationToken);
        return CreatedAtAction(nameof(Get), new { groupId = group.Id }, group);
    }

    /// <summary>
    /// Confere a senha antes de entrar: devolve o nome do grupo, quantas pessoas há e os perfis sem conta que dá para assumir.
    /// Senha errada, desativada ou de grupo excluído respondem igual (<c>group.invalid_code</c>).
    /// </summary>
    [HttpPost("lookup")]
    [EnableRateLimiting(RateLimitPolicies.GroupJoin)]
    public async Task<ActionResult<GroupPreviewDto>> Lookup([FromBody] LookupGroupRequest request, CancellationToken cancellationToken) =>
        await groups.LookupAsync(User.RequireUserId(), request, cancellationToken);

    /// <summary>
    /// Entra no grupo com a senha. Com <c>claimMemberId</c>, a pessoa assume um perfil sem conta e herda o histórico dele.
    /// Quem já é membro recebe o grupo como está (idempotente).
    /// </summary>
    [HttpPost("join")]
    [EnableRateLimiting(RateLimitPolicies.GroupJoin)]
    public async Task<ActionResult<GroupDetailDto>> Join([FromBody] JoinGroupRequest request, CancellationToken cancellationToken) =>
        await groups.JoinAsync(User.RequireUserId(), request, cancellationToken);

    /// <summary>O grupo, a senha (se ativa) e os membros.</summary>
    [HttpGet("{groupId:guid}")]
    public async Task<ActionResult<GroupDetailDto>> Get(Guid groupId, CancellationToken cancellationToken) =>
        await groups.GetAsync(User.RequireUserId(), groupId, cancellationToken);

    /// <summary>Muda o nome e/ou liga e desliga a senha. Exige administrador ou dono.</summary>
    [HttpPatch("{groupId:guid}")]
    public async Task<ActionResult<GroupDetailDto>> Update(Guid groupId, [FromBody] UpdateGroupRequest request, CancellationToken cancellationToken) =>
        await groups.UpdateAsync(User.RequireUserId(), groupId, request, cancellationToken);

    /// <summary>Exclui o grupo (só o dono). Exige <c>{ "confirmation": "EXCLUIR" }</c>.</summary>
    [HttpDelete("{groupId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Delete(Guid groupId, [FromBody] DeleteGroupRequest request, CancellationToken cancellationToken)
    {
        await groups.DeleteAsync(User.RequireUserId(), groupId, request, cancellationToken);
        return NoContent();
    }

    /// <summary>Gera uma senha nova (e a ativa); a antiga deixa de valer na hora. Exige administrador ou dono.</summary>
    [HttpPost("{groupId:guid}/invite-code")]
    public async Task<ActionResult<GroupDetailDto>> RegenerateInviteCode(Guid groupId, CancellationToken cancellationToken) =>
        await groups.RegenerateInviteCodeAsync(User.RequireUserId(), groupId, cancellationToken);

    /// <summary>Passa a propriedade para outro membro com conta; o dono atual vira administrador. Só o dono.</summary>
    [HttpPost("{groupId:guid}/transfer-ownership")]
    public async Task<ActionResult<GroupDetailDto>> TransferOwnership(Guid groupId, [FromBody] TransferOwnershipRequest request, CancellationToken cancellationToken) =>
        await members.TransferOwnershipAsync(User.RequireUserId(), groupId, request, cancellationToken);

    /// <summary>Sai do grupo. O dono não pode sair: precisa transferir a propriedade ou excluir o grupo.</summary>
    [HttpDelete("{groupId:guid}/members/me")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Leave(Guid groupId, CancellationToken cancellationToken)
    {
        await groups.LeaveAsync(User.RequireUserId(), groupId, cancellationToken);
        return NoContent();
    }

    /// <summary>Cria um membro sem conta (nome e avatar), para quem ainda não usa o app. Exige administrador ou dono.</summary>
    [HttpPost("{groupId:guid}/members")]
    [ProducesResponseType<MemberDto>(StatusCodes.Status201Created)]
    public async Task<IActionResult> AddProfile(Guid groupId, [FromBody] AddProfileRequest request, CancellationToken cancellationToken)
    {
        var member = await members.AddProfileAsync(User.RequireUserId(), groupId, request, cancellationToken);
        return StatusCode(StatusCodes.Status201Created, member);
    }

    /// <summary>
    /// Altera um membro: nome e avatar (só de perfis sem conta; administrador ou dono) ou papel
    /// (<c>admin</c>/<c>member</c>; só o dono).
    /// </summary>
    [HttpPatch("{groupId:guid}/members/{memberId:guid}")]
    public async Task<ActionResult<MemberDto>> UpdateMember(Guid groupId, Guid memberId, [FromBody] UpdateMemberRequest request, CancellationToken cancellationToken) =>
        await members.UpdateAsync(User.RequireUserId(), groupId, memberId, request, cancellationToken);

    /// <summary>
    /// Remove um membro. O dono remove qualquer um (menos a si mesmo); administradores removem membros comuns e perfis sem conta.
    /// </summary>
    [HttpDelete("{groupId:guid}/members/{memberId:guid}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RemoveMember(Guid groupId, Guid memberId, CancellationToken cancellationToken)
    {
        await members.RemoveAsync(User.RequireUserId(), groupId, memberId, cancellationToken);
        return NoContent();
    }

    /// <summary>
    /// Envia a foto de um perfil sem conta (mesmas regras da foto da conta: JPEG, PNG, WebP ou GIF de até 3 MB, recortada em
    /// quadrado e sem metadados). Campo do formulário: <c>file</c>. Exige administrador ou dono.
    /// </summary>
    [HttpPut("{groupId:guid}/members/{memberId:guid}/avatar")]
    [Consumes("multipart/form-data")]
    [AvatarUploadLimit]
    [EnableRateLimiting(RateLimitPolicies.Upload)]
    public async Task<ActionResult<MemberDto>> SetMemberAvatar(Guid groupId, Guid memberId, IFormFile file, CancellationToken cancellationToken)
    {
        await using var stream = file.OpenReadStream();
        return await members.SetPhotoAsync(User.RequireUserId(), groupId, memberId, stream, cancellationToken);
    }

    /// <summary>Remove a foto de um perfil sem conta e volta ao avatar padrão. Exige administrador ou dono.</summary>
    [HttpDelete("{groupId:guid}/members/{memberId:guid}/avatar")]
    public async Task<ActionResult<MemberDto>> ClearMemberAvatar(Guid groupId, Guid memberId, CancellationToken cancellationToken) =>
        await members.ClearPhotoAsync(User.RequireUserId(), groupId, memberId, cancellationToken);
}
