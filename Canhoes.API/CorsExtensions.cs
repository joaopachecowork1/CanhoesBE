using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Net;

namespace Canhoes.Api
{
    public static class CorsExtensions
    {
        private const string PolicyName = "Frontend";

        public static IServiceCollection AddFrontendCors(this IServiceCollection services, IConfiguration config)
        {
            var configuredOrigins = config.GetSection("Cors:Origins").Get<string[]>() ?? Array.Empty<string>();
            var csvOrigins = SplitCsv(config["Cors:OriginsCsv"]);
            var allowedOrigins = configuredOrigins
                .Concat(csvOrigins)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeOrigin)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (allowedOrigins.Length == 0)
            {
                allowedOrigins = ["http://localhost:3000"];
            }

            var allowedOriginSuffixes = SplitCsv(config["Cors:AllowedOriginSuffixesCsv"])
                .Select(x => x.Trim().TrimStart('.'))
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            services.AddCors(opt =>
            {
                opt.AddPolicy(PolicyName, policy =>
                {
                    policy.SetIsOriginAllowed(origin =>
                          IsAllowedOrigin(origin, allowedOrigins, allowedOriginSuffixes))
                        .AllowAnyHeader()
                        .AllowAnyMethod()
                        .AllowCredentials();
                });
            });
            return services;
        }

        public static IApplicationBuilder UseFrontendCors(this IApplicationBuilder app)
        {
            return app.UseCors(PolicyName);
        }

        private static string[] SplitCsv(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return Array.Empty<string>();

            return value
                .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        }

        private static string NormalizeOrigin(string origin)
        {
            if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            {
                return origin.Trim();
            }

            var builder = new UriBuilder(uri.Scheme, uri.Host, uri.IsDefaultPort ? -1 : uri.Port);
            return builder.Uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        }

        private static bool IsAllowedOrigin(
            string origin,
            IReadOnlyCollection<string> allowedOrigins,
            IReadOnlyCollection<string> allowedOriginSuffixes)
        {
            var normalizedOrigin = NormalizeOrigin(origin);
            if (allowedOrigins.Contains(normalizedOrigin, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!Uri.TryCreate(normalizedOrigin, UriKind.Absolute, out var uri))
            {
                return false;
            }

            var host = uri.Host;
            if (IPAddress.TryParse(host, out _))
            {
                return false;
            }

            return allowedOriginSuffixes.Any(suffix =>
                host.Equals(suffix, StringComparison.OrdinalIgnoreCase) ||
                host.EndsWith($".{suffix}", StringComparison.OrdinalIgnoreCase));
        }
    }
}
