namespace BioFXAPI.Models
{
    public class LoginRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
    }

    public class RegisterRequest
    {
        public string Email { get; set; }
        public string Password { get; set; }
        public string Nombre { get; set; }
        public string Apellido { get; set; }
        public string Telefono { get; set; }
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; }
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; }
        public string Token { get; set; }
        public string NewPassword { get; set; }
    }

    public class ChangeEmailRequest
    {
        public string NuevoEmail { get; set; }
    }

    public class VerifyEmailChangeRequest
    {
        public string Token { get; set; }
        public string NuevoEmail { get; set; }
    }

    public class ChangePasswordRequest
    {
        public string CurrentPassword { get; set; }
        public string NewPassword { get; set; }
    }

}