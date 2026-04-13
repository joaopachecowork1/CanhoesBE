using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace Canhoes.Api.Auth
{
    public static class JwtAuthExtensions
    {
        public static IServiceCollection AddGoogleJwtAuth(this IServiceCollection services, IConfiguration config)
        {
            var clientId = config["Auth:Google:ClientId"];
            if (string.IsNullOrWhiteSpace(clientId))
                throw new InvalidOperationException("Auth:Google:ClientId is missing.");

            var validIssuers = new[] { "https://accounts.google.com", "accounts.google.com" };

            services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
                .AddJwtBearer(options =>
                {
                    options.Authority = "https://accounts.google.com";
                    options.RequireHttpsMetadata = true;
                    options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true,
                        ValidIssuers = validIssuers,
                        ValidateAudience = true,
                        ValidAudience = clientId,
                        ValidateLifetime = true,
                        ClockSkew = TimeSpan.FromMinutes(2)
                    };

                    options.Events = new JwtBearerEvents
                    {
                        OnAuthenticationFailed = ctx =>
                        {
                            var logger = ctx.HttpContext.RequestServices
                                .GetRequiredService<ILoggerFactory>()
                                .CreateLogger("GoogleJwtAuth");

                            logger.LogWarning(
                                ctx.Exception,
                                "Google token validation failed for request {Method} {Path}.",
                                ctx.HttpContext.Request.Method,
                                ctx.HttpContext.Request.Path);

                            return Task.CompletedTask;
                        }
                    };
                });

            return services;
        }
    }
}
