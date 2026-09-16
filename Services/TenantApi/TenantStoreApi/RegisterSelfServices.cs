using Asp.Versioning;
using MassTransit;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Common.Lib.Security;
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
            var authUrl = builder.Configuration["AuthUrl"]?.ToString() ?? "";
            options.Address = new Uri(authUrl);
        }).AddHeaderPropagation();
        builder.Services.AddGrpcClient<GetUserInfoService.GetUserInfoServiceClient>(options =>
        {
            var authUrl = builder.Configuration["AuthUrl"]?.ToString() ?? "";
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

        builder.Services.AddDbContext<TenantContext>(options =>
        {
            var connectionString = builder.Configuration.GetConnectionString("TenantConnection");
            var sqlVersion = ServerVersion.AutoDetect(connectionString);//new MySqlServerVersion(new Version(9, 2, 0))
            options.UseMySql(connectionString, sqlVersion);
            options.AddInterceptors(new SoftDeleteInterceptor());
            options.UseLazyLoadingProxies(true);
        });

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

        // Resolve JwksClient lazily from the real (post-Build) app container instead of a
        // throwaway one built eagerly here (see the equivalent Auth Api fix for why that
        // pattern is unsafe for stateful/persisted singletons — JwksClient itself is a plain
        // stateless HTTP fetcher, so this is a consistency/robustness change, not a correctness
        // fix, but keeping the same idiom everywhere avoids the pattern looking safe-by-default).
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IHttpClientFactory>((options, httpClientFactory) =>
            {
                var authUrl = (builder.Configuration["AuthUrl"] ?? "").TrimEnd('/') + "/";
                var allowLegacyHmac = builder.Configuration.GetValue<bool?>("JwtSettings:AllowLegacyHmacValidation") ?? true;
                var legacySigningKey = builder.Configuration["JwtSettings:SigningKey"];

                List<SecurityKey> cachedKeys = new();
                DateTime cacheExpiry = DateTime.MinValue;
                object cacheLock = new();

                options.TokenValidationParameters.IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                {
                    bool stale;
                    lock (cacheLock) stale = DateTime.UtcNow > cacheExpiry;

                    if (stale)
                    {
                        try
                        {
                            var client = httpClientFactory.CreateClient();
                            var json = client.GetStringAsync($"{authUrl}.well-known/jwks.json").GetAwaiter().GetResult();
                            var jwks = new JsonWebKeySet(json);
                            lock (cacheLock)
                            {
                                cachedKeys = jwks.GetSigningKeys().ToList();
                                cacheExpiry = DateTime.UtcNow.AddMinutes(10);
                            }
                        }
                        catch { }
                    }

                    List<SecurityKey> keys;
                    lock (cacheLock) keys = cachedKeys.ToList();

                    if (allowLegacyHmac && !string.IsNullOrEmpty(legacySigningKey))
                        keys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(legacySigningKey)));

                    return keys;
                };
            });
    }
}
