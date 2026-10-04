using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using RonatIa.Games.Application.Abstractions;
using RonatIa.Games.Application.Options;

namespace RonatIa.Games.Infrastructure.Security;

/// <summary>
/// HMAC-SHA256 do telefone (E.164) com o segredo do servidor. Como números de telefone têm pouca entropia, um hash simples
/// seria quebrado por força bruta; com o segredo (<c>Auth:PhonePepper</c>) fora do banco, um vazamento do banco sozinho não revela os números.
/// </summary>
public sealed class HmacPhoneHasher(IOptions<AuthOptions> options) : IPhoneHasher
{
    private readonly Lazy<byte[]> _key = new(() => Convert.FromBase64String(options.Value.PhonePepper));

    public byte[] Hash(string e164) => HMACSHA256.HashData(_key.Value, Encoding.UTF8.GetBytes(e164));
}
