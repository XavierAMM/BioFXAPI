using System;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Cryptography.KeyDerivation;

namespace BioFXAPI.Services
{
    public class PasswordService
    {
        private const int SaltSize = 128 / 8; // 128 bits
        private const int HashSize = 256 / 8; // 256 bits
        private const int IterationCount = 10000;

        public const int MaxFailedAttempts = 5;
        public const int LockoutMinutes = 15;

        public string HashPassword(string password)
        {
            // Generate a random salt
            byte[] salt = new byte[SaltSize];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            // Hash the password
            byte[] hash = KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: IterationCount,
                numBytesRequested: HashSize);

            // Combine salt and hash
            byte[] hashBytes = new byte[SaltSize + HashSize];
            Array.Copy(salt, 0, hashBytes, 0, SaltSize);
            Array.Copy(hash, 0, hashBytes, SaltSize, HashSize);

            // Convert to base64
            return Convert.ToBase64String(hashBytes);
        }

        public bool VerifyPassword(string password, string hashedPassword)
        {
            // Extract bytes
            byte[] hashBytes = Convert.FromBase64String(hashedPassword);

            // Extract salt
            byte[] salt = new byte[SaltSize];
            Array.Copy(hashBytes, 0, salt, 0, SaltSize);

            // Compute hash of the provided password
            byte[] hash = KeyDerivation.Pbkdf2(
                password: password,
                salt: salt,
                prf: KeyDerivationPrf.HMACSHA256,
                iterationCount: IterationCount,
                numBytesRequested: HashSize);

            // Compare hashes
            for (int i = 0; i < HashSize; i++)
            {
                if (hashBytes[i + SaltSize] != hash[i])
                {
                    return false;
                }
            }

            return true;
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