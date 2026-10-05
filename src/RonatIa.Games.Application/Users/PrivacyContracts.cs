using RonatIa.Games.Application.Auth;
using RonatIa.Games.Domain.Groups;

namespace RonatIa.Games.Application.Users;

public sealed record ExportedProfileDto(
    Guid Id,
    string DisplayName,
    AvatarDto Avatar,
    string PhoneLast4,
    DateTimeOffset CreatedAt,
    DateTimeOffset? LastLoginAt,
    DateTimeOffset? TermsAcceptedAt,
    string? TermsVersion);

public sealed record ExportedLoginDto(
    Guid Id,
    string? DeviceLabel,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastUsedAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? RevokedAt,
    string? RevokedReason);

public sealed record ExportedMembershipDto(
    Guid MemberId,
    Guid GroupId,
    string GroupName,
    GroupRole Role,
    MemberStatus Status,
    DateTimeOffset JoinedAt,
    DateTimeOffset? LeftAt);

public sealed record ExportedResultDto(
    Guid SessionId,
    Guid GroupId,
    string GameId,
    DateTimeOffset FinishedAt,
    int? Team,
    int Rank,
    int Score,
    bool IsWinner);

/// <summary>Cópia dos dados pessoais da própria pessoa (direito de acesso e portabilidade, LGPD). Não traz dados de outras pessoas.</summary>
/// <param name="Profile">O telefone nunca é guardado em claro: só os 4 últimos dígitos existem.</param>
public sealed record PersonalDataExportDto(
    DateTimeOffset ExportedAt,
    ExportedProfileDto Profile,
    IReadOnlyList<ExportedLoginDto> Logins,
    IReadOnlyList<ExportedMembershipDto> Memberships,
    IReadOnlyList<ExportedResultDto> Results);
