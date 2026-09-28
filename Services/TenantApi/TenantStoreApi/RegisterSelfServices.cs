using Asp.Versioning;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Common.Lib.Security;
using TenantStoreApi.Core.Auditing;
using System.Net;
using System.Text;
using TenantStoreApi.Core.Providers;
using TenantStoreApi.Core.Utilities;
using TenantStoreApi.Infrastructure;

public static class ServiceRegistrations
{
    public static void RegisterSelfServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddGrpcClient<CheckEmailService.CheckEmailServiceClient>(options =>
        {
            var authUrl = builder.Configuration["Grpc_AuthUrl"]?.ToString() ?? "";
            options.Address = new Uri(authUrl);
        }).AddHeaderPropagation();
        builder.Services.AddGrpcClient<GetUserInfoService.GetUserInfoServiceClient>(options =>
        {
            var authUrl = builder.Configuration["Grpc_AuthUrl"]?.ToString() ?? "";
            options.Address = new Uri(authUrl);
        }).AddHeaderPropagation();
        builder.Services.AddScoped<UserInfoService>();
        builder.Services.AddHttpClient<JwksClient>(client =>
        {
            var authUrl = builder.Configuration["AuthUrl"]?.ToString() ?? "";
            client.BaseAddress = new Uri(authUrl);
        });
        builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
        builder.Services.Configure<HMacSetting>(builder.Configuration.GetSection("HMacSettings"));
        builder.Services.Configure<CryptoSetting>(builder.Configuration.GetSection("Crypto"));
        builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMqSettings"));
        builder.Services.AddHeaderPropagation(options =>
        {
            options.Headers.Add("User-Agent");
            options.Headers.Add("Authorization");
            options.Headers.Add("X-Tenant-ID");
            options.Headers.Add("X-Api-Key");
        });
        builder.Services.AddGrpc();
        builder.Services.AddLogging();
        builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
        builder.Services.AddScoped<ITenantProvider, TenantProviderAccessor>();
        var sqlVersion = new MySqlServerVersion(new Version(8, 4, 8));
        builder.Services.AddDbContext<TenantContext>(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("TenantConnection");
            options.UseMySql(connectionString, sqlVersion);
            options.AddInterceptors(new SoftDeleteInterceptor());
            options.UseLazyLoadingProxies(true);
        });

        builder.Services.AddTenantAuditTrail(builder.Configuration);

        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.WithOrigins(
                    "http://localhost:5173",
                    "http://localhost:4200",
                    "http://159.89.194.81:8001",
                    "http://159.89.194.81:8002",
                    "http://159.89.194.81:8003",
                    "http://165.232.166.164:8082",
                    "http://165.232.166.164:8083",
                    "http://165.232.166.164:8084",
                    "http://165.232.166.164:8085",
                    "http://165.232.166.164:8086",
                    "https://hris.onepunch.site",
                    "https://hris-dev.onepunch.site",
                    "https://hris-staging.onepunch.site",
                    "https://api.onepunch.site")
                      .AllowCredentials()
                      .AllowAnyMethod()
                      .AllowAnyHeader();
            });
        });
        builder.Services
            .AddApiVersioning(options =>
            {
                options.ReportApiVersions = true;
                options.AssumeDefaultVersionWhenUnspecified = true;
                options.DefaultApiVersion = new ApiVersion(1, 0);
            })
            .AddApiExplorer(options =>
            {
                options.GroupNameFormat = "'v'VVV";
                options.SubstituteApiVersionInUrl = true;
            });
        builder.Services.AddAuthorizationBuilder()
         .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

        builder.Services.AddAuthentication(options =>
        {
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
        })
         .AddJwtBearer(options =>
         {
             // AuthApi's JwtService.CreateTokenAsync mints "sub"/"email" as the short registered
             // JWT claim names -- without this, ASP.NET Core's default inbound map silently
             // rewrites them to long ClaimTypes.* URIs when building the ClaimsPrincipal. See
             // HttpRequestExtensions.GetUserId/GetRequiredUserId below, which now read "sub"
             // directly to match.
             options.MapInboundClaims = false;
             options.TokenValidationParameters = new TokenValidationParameters
             {
                 ValidateIssuer = true,
                 ValidIssuer = "Onepunch",
                 ValidateAudience = true,
                 ValidAudience = "Onepunch.AuthService",
                 ValidateLifetime = true,
                 ValidateIssuerSigningKey = true
             };
             options.Events = new JwtBearerEvents
             {
                 OnAuthenticationFailed = context =>
                 {
                     Console.WriteLine("Auth failed: " + context.Exception.Message);
                     return Task.CompletedTask;
                 },

                 OnChallenge = async context =>
                 {
                     // Skip the default response
                     context.HandleResponse();

                     context.Response.StatusCode = 401;
                     context.Response.ContentType = "application/json";

                     var errorDetail = new ProblemDetails
                     {
                         Type = $"https://httpstatuses.com/{401}",
                         Title = "Unauthorized",
                         Status = (int)HttpStatusCode.Unauthorized,
                         Detail = "Unauthorized. Token is missing or invalid.",
                         Instance = $"{context.Request.Method} {context.Request.Path}"
                     };
                     var response = new ResponseModel<ProblemDetails>
                     {
                         Message = errorDetail.Detail,
                         Status = (int)HttpStatusCode.Unauthorized,
                         Data = errorDetail
                     };
                     await context.Response.WriteAsJsonAsync(response);
                 },

                 OnForbidden = async context =>
                 {
                     context.Response.StatusCode = 403;
                     context.Response.ContentType = "application/json";

                     var errorDetail = new ProblemDetails
                     {
                         Type = $"https://httpstatuses.com/{403}",
                         Title = "Forbidden",
                         Status = (int)HttpStatusCode.Forbidden,
                         Detail = "Forbidden. You do not have permission to access this resource.",
                         Instance = $"{context.Request.Method} {context.Request.Path}"
                     };
                     var response = new ResponseModel<ProblemDetails>
                     {
                         Message = errorDetail.Detail,
                         Status = (int)HttpStatusCode.Unauthorized,
                         Data = errorDetail
                     };

                     await context.Response.WriteAsJsonAsync(response);
                 }
             };
         });

        // JWKS keys: in-process -> shared Redis copy -> single-flight fetch with failure backoff.
        // Replaces an inline resolver that blocked every request on its own HTTP call whenever
        // Auth was slow or down (see JwksKeyCache).
        builder.Services.AddJwksKeyCache(
            builder.Configuration["AuthUrl"] ?? "",
            builder.Configuration.GetConnectionString("Redis"));
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<JwksKeyCache>((options, jwksKeyCache) =>
            {
                options.TokenValidationParameters.IssuerSigningKeyResolver = JwksServiceCollectionExtensions.CreateSigningKeyResolver(
                    jwksKeyCache,
                    builder.Configuration["JwtSettings:SigningKey"],
                    builder.Configuration.GetValue<bool?>("JwtSettings:AllowLegacyHmacValidation") ?? true);
            });
    }
}
