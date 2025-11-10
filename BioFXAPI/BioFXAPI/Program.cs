using BioFXAPI.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using Serilog;
using System.Text;


var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

builder.Services.AddControllers();

var jwtSettings = builder.Configuration.GetSection("Jwt");
builder.Configuration.AddUserSecrets<Program>();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.RequireHttpsMetadata = true;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(jwtSettings["Secret"])),
            ValidateIssuer = false,
            ValidateAudience = false,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.Zero
        };

        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                if (string.IsNullOrEmpty(context.Token) &&
                    context.Request.Cookies.TryGetValue("biofx_auth", out var cookieToken))
                {
                    context.Token = cookieToken;
                }
                return Task.CompletedTask;
            }
        };
    });

builder.Services.AddAuthorization();


// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()
            .SetPreflightMaxAge(TimeSpan.FromHours(1));

    });
});

builder.Services.AddRateLimiter(options =>
{
    options.AddFixedWindowLimiter("api", s =>
    {
        s.PermitLimit = 60;
        s.Window = TimeSpan.FromMinutes(1);
        s.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("StrictLogin", o =>
    {
        o.PermitLimit = 5; // 5 intentos/min
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });

    options.AddFixedWindowLimiter("StrictPasswordOps", o =>
    {
        o.PermitLimit = 3; // 3 req/min
        o.Window = TimeSpan.FromMinutes(1);
        o.QueueLimit = 0;
    });
});

var emailSettings = builder.Configuration.GetSection("EmailSettings");
if (string.IsNullOrEmpty(emailSettings["SmtpServer"]))
{
    throw new InvalidOperationException("EmailSettings:SmtpServer no configurado");
}


builder.Services.AddScoped<PasswordService>();
builder.Services.AddScoped<EmailService>();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "BioFX API", Version = "v1" });

    // Configuración para autenticación JWT en Swagger
    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Example: \"Authorization: Bearer {token}\"",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            new string[] {}
        }
    });
});

builder.Services.AddHealthChecks();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Debug()
    .WriteTo.Console()
    .WriteTo.File("Logs/biofxapi-.log", rollingInterval: RollingInterval.Day)
    .CreateLogger();

builder.Host.UseSerilog();


var app = builder.Build();
app.UseHealthChecks("/health");
if (!app.Environment.IsDevelopment())
{
    app.UseHsts();
}

if (app.Environment.IsDevelopment()) { app.UseSwagger(); app.UseSwaggerUI(); }

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseWhen(ctx => !HttpMethods.IsOptions(ctx.Request.Method), branch =>
{
    branch.UseRateLimiter();
});
app.UseRateLimiter();
app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers().RequireRateLimiting("api");


app.Run();
