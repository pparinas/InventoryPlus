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

        public async Task<InviteToken> GenerateInviteAsync(Guid createdBy)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .Replace("+", "-").Replace("/", "_").TrimEnd('=');

            var invite = new InviteToken
            {
                Id = Guid.NewGuid(),
                Token = token,
                CreatedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddMinutes(10),
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
                var update = _supabase.From<InviteToken>()
                    .Where(t => t.Token == token)
                    .Set(t => t.IsUsed, true);

                if (!string.IsNullOrEmpty(email))
                    update = update.Set(t => t.UsedByEmail, email);

                await update.Update();
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
