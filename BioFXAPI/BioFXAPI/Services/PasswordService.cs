using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace BioFXAPI.Services
{
    public class PasswordService
    {
        private const int SaltSize = 128 / 8; // 128 bits
        private const int HashSize = 256 / 8; // 256 bits
        private const int IterationCount = 310_000;       // OWASP 2023: PBKDF2-SHA256
        private const int LegacyIterationCount = 10_000;  // hashes existentes en BD
        private const byte FormatVersion = 0x01;          // prefijo que distingue formato nuevo

        public const int MaxFailedAttempts = 5;
        public const int LockoutMinutes = 15;

        public string HashPassword(string password)
        {
            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);

            byte[] hash = KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: IterationCount,
                numBytesRequested: HashSize);

            // Formato nuevo: [FormatVersion (1 byte)][salt (16)][hash (32)] = 49 bytes
            byte[] hashBytes = new byte[1 + SaltSize + HashSize];
            hashBytes[0] = FormatVersion;
            Array.Copy(salt, 0, hashBytes, 1, SaltSize);
            Array.Copy(hash, 0, hashBytes, 1 + SaltSize, HashSize);

            return Convert.ToBase64String(hashBytes);
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            byte[] hashBytes = Convert.FromBase64String(hashedPassword);

            byte[] salt;
            byte[] storedHash;
            int iterations;

            if (hashBytes.Length == SaltSize + HashSize)
            {
                // Formato legado (48 bytes): [salt (16)][hash (32)] — 10,000 iteraciones
                iterations = LegacyIterationCount;
                salt = new byte[SaltSize];
                storedHash = new byte[HashSize];
                Array.Copy(hashBytes, 0, salt, 0, SaltSize);
                Array.Copy(hashBytes, SaltSize, storedHash, 0, HashSize);
            }
            else
            {
                // Formato nuevo (49 bytes): [version (1)][salt (16)][hash (32)] — 310,000 iteraciones
                iterations = IterationCount;
                salt = new byte[SaltSize];
                storedHash = new byte[HashSize];
                Array.Copy(hashBytes, 1, salt, 0, SaltSize);
                Array.Copy(hashBytes, 1 + SaltSize, storedHash, 0, HashSize);
            }

            byte[] hash = KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: iterations,
                numBytesRequested: HashSize);

            return CryptographicOperations.FixedTimeEquals(storedHash, hash);
        }


        public bool IsAccountLocked(DateTime? lockoutEnd)
        {
            return lockoutEnd.HasValue && lockoutEnd.Value > DateTime.UtcNow;
        }

        public DateTime? CalculateLockoutEnd(int failedAttempts)
        {
            const int MaxFailedAttempts = 5;
            const int LockoutMinutes = 15;

            return failedAttempts >= MaxFailedAttempts ?
                DateTime.UtcNow.AddMinutes(LockoutMinutes) : null;
        }

        public bool IsPasswordStrong(string password)
        {
            // Minimum 8 characters
            if (password.Length < 8) return false;

            // At least one uppercase letter
            if (!password.Any(char.IsUpper)) return false;

            // At least one lowercase letter
            if (!password.Any(char.IsLower)) return false;

            // At least one digit
            if (!password.Any(char.IsDigit)) return false;

            return true;
        }
    }
}