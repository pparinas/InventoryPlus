using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using InventoryPlus.Models;

namespace InventoryPlus.Services
{
    public class InviteTokenService
    {
        private readonly Supabase.Client _supabase;

        public InviteTokenService(Supabase.Client supabase)
        {
            _supabase = supabase;
        }

        public async Task<InviteToken> GenerateInviteAsync(Guid createdBy, int expiryHours = 24)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');

            var invite = new InviteToken
            {
                Id = Guid.NewGuid(),
                Token = token,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddHours(expiryHours),
                IsUsed = false,
                CreatedBy = createdBy
            };

            await _supabase.From<InviteToken>().Insert(invite);
            return invite;
        }

        public async Task<InviteToken?> ValidateTokenAsync(string token)
        {
            try
            {
                var resp = await _supabase.From<InviteToken>()
                    .Where(t => t.Token == token)
                    .Get();

                var invite = resp.Models.FirstOrDefault();
                if (invite == null) return null;
                if (invite.IsUsed) return null;
                if (invite.ExpiresAt < DateTime.UtcNow) return null;

                return invite;
            }
            catch
            {
                return null;
            }
        }

        public async Task MarkTokenUsedAsync(string token, string email = "")
        {
            try
            {
                await _supabase.Rpc("mark_invite_token_used", new Dictionary<string, object>
                {
                    { "token_value", token },
                    { "used_email", email ?? "" }
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MarkTokenUsed error: {ex.Message}");
            }
        }

        public async Task<List<InviteToken>> GetAllTokensAsync()
        {
            try
            {
                var resp = await _supabase.From<InviteToken>()
                    .Order(t => t.CreatedAt, Supabase.Postgrest.Constants.Ordering.Descending)
                    .Get();
                return resp.Models;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"GetAllTokens error: {ex.Message}");
                return new();
            }
        }
    }
}
