using System.Collections.Concurrent;

namespace BioFXAPI.Services
{
    public class TokenBlacklistService
    {
        private readonly ConcurrentDictionary<string, DateTimeOffset> _revokedTokens = new();

        public void Revoke(string jti, DateTimeOffset expiresAt)
            => _revokedTokens[jti] = expiresAt;

        public bool IsRevoked(string jti)
        {
            if (!_revokedTokens.TryGetValue(jti, out var expiresAt))
                return false;

            // Auto-limpiar si el token ya expiró de todas formas
            if (DateTimeOffset.UtcNow > expiresAt)
            {
                _revokedTokens.TryRemove(jti, out _);
                return false;
            }

            return true;
        }

        public void Cleanup()
        {
            var now = DateTimeOffset.UtcNow;
            foreach (var kvp in _revokedTokens.Where(kvp => now > kvp.Value))
                _revokedTokens.TryRemove(kvp.Key, out _);
        }
    }
}
