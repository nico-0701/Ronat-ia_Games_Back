using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Mvc.Testing;
using RonatIa.Games.Application.Auth;
using RonatIa.Games.Application.Groups;

namespace RonatIa.Games.Api.Tests.Infrastructure;

/// <summary>As mesmas opções do servidor: camelCase e enums como texto.</summary>
public static class TestJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static async Task<T> ReadAsAsync<T>(this HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(body, Options) ?? throw new InvalidOperationException($"Corpo vazio: {body}");
    }
}

/// <summary>Uma pessoa de teste: conta criada, com um cliente HTTP já autenticado.</summary>
public sealed class Person
{
    public required AuthResponse Auth { get; init; }

    public required HttpClient Client { get; init; }

    /// <summary>O telefone com que a conta foi criada (para entrar de novo em outro "aparelho").</summary>
    public string? Phone { get; init; }

    public Guid UserId => Auth.User.Id;

    public string Name => Auth.User.DisplayName;

    public Task<HttpResponseMessage> GetAsync(string url) => Client.GetAsync(url);

    public Task<HttpResponseMessage> PostAsync(string url, object? body = null) => Client.PostAsJsonAsync(url, body ?? new { });

    public Task<HttpResponseMessage> PatchAsync(string url, object body) => Client.PatchAsJsonAsync(url, body);

    public Task<HttpResponseMessage> PutFileAsync(string url, byte[] bytes, string fileName = "foto.jpg", string contentType = "image/jpeg") =>
        Client.PutAsync(url, TestImages.Form(bytes, fileName, contentType));

    public Task<HttpResponseMessage> DeleteAsync(string url, object? body = null) =>
        Client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        });
}

public static class GroupTestHelpers
{
    public static async Task<Person> NewPersonAsync(this WebApplicationFactory<Program> factory, string name = "Pessoa Teste")
    {
        var phone = TestPhones.Next();
        var auth = await factory.CreateClient().RegisterAsync(phone, name);
        return new Person { Auth = auth, Client = factory.ClientFor(auth.AccessToken), Phone = phone };
    }

    public static async Task<GroupDetailDto> CreateGroupAsync(this Person person, string name = "Grupo de Teste")
    {
        var response = await person.PostAsync("/api/v1/groups", new { name });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsAsync<GroupDetailDto>();
    }

    public static Task<HttpResponseMessage> JoinRawAsync(this Person person, string code, Guid? claimMemberId = null) =>
        person.PostAsync("/api/v1/groups/join", new { code, claimMemberId });

    public static async Task<GroupDetailDto> JoinAsync(this Person person, string code, Guid? claimMemberId = null)
    {
        var response = await person.JoinRawAsync(code, claimMemberId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<GroupDetailDto>();
    }

    public static async Task<GroupDetailDto> GetGroupAsync(this Person person, Guid groupId)
    {
        var response = await person.GetAsync($"/api/v1/groups/{groupId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<GroupDetailDto>();
    }

    public static async Task<IReadOnlyList<GroupSummaryDto>> ListGroupsAsync(this Person person)
    {
        var response = await person.GetAsync("/api/v1/groups");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadAsAsync<List<GroupSummaryDto>>();
    }

    public static async Task<MemberDto> AddProfileAsync(this Person person, Guid groupId, string name, string? preset = null)
    {
        var response = await person.PostAsync($"/api/v1/groups/{groupId}/members", new { displayName = name, avatarPreset = preset });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadAsAsync<MemberDto>();
    }

    /// <summary>Cria um grupo com o dono e entra com as pessoas informadas (todas como membros comuns).</summary>
    public static async Task<GroupDetailDto> GroupWithAsync(this Person owner, params Person[] others)
    {
        var group = await owner.CreateGroupAsync();
        foreach (var other in others)
        {
            await other.JoinAsync(group.InviteCode!);
        }

        return await owner.GetGroupAsync(group.Id);
    }

    /// <summary>Entra de novo com o telefone (um token novo): necessário depois de avançar muito o relógio de teste.</summary>
    public static async Task<Person> ReloginAsync(this Person person, WebApplicationFactory<Program> factory)
    {
        var auth = await factory.CreateClient().LoginAsync(person.Phone!);
        return new Person { Auth = auth, Client = factory.ClientFor(auth.AccessToken), Phone = person.Phone };
    }

    public static MemberDto MemberOf(this GroupDetailDto group, Person person) =>
        group.Members.Single(member => member.HasAccount && member.DisplayName == person.Name);

    public static string Url(this GroupDetailDto group, string suffix = "") => $"/api/v1/groups/{group.Id}{suffix}";
}
