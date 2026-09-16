using Asp.Versioning.Conventions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Auth.Core;
using Onepunch.Auth.Infrastructure;
using Onepunch.Common.Lib;
using Onepunch.Common.Lib.Cache;
using OnePunch.Auth.Core.Providers;
using StackExchange.Redis;
using System.Net;
using System.Text;

namespace OnePunch.Auth.Api;

public static class ServiceRegistrations
{
    public static void RegisterSelftServices(this WebApplicationBuilder builder)
    {
        //builder.Services.AddHostedService<InspectTenantRequestStateWatcher>();/
        builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
        builder.Services.Configure<HMacSetting>(builder.Configuration.GetSection("HMacSettings"));
        builder.Services.Configure<CryptoSetting>(builder.Configuration.GetSection("Crypto"));
        builder.Services.Configure<Domains>(builder.Configuration.GetSection("Domains"));
        builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("JwtSettings"));
        builder.Services.Configure<RabbitMqSettings>(builder.Configuration.GetSection("RabbitMqSettings"));
        builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
        ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
        builder.Services.AddScoped<ICacheService, RedisCacheService>();
        builder.Services.AddHeaderPropagation(options =>
        {
            options.Headers.Add("User-Agent");
            options.Headers.Add("Authorization");
            options.Headers.Add("X-Tenant-ID");
            options.Headers.Add("X-Api-Key");
        });

        builder.Services.AddGrpc();
        builder.Services.AddGrpcClient<GetTenantService.GetTenantServiceClient>(options =>
        {
            var tenantUrl = builder.Configuration["Domains:TenantUrl"]?.ToString() ?? "";
            options.Address = new Uri(tenantUrl);
        });
        builder.Services.AddDataProtection();
        builder.Services.AddLogging();
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<ITenantProvider, TenantProviderAccessor>();
        builder.Services.AddIdentity<User, Domain.Entities.Role>(options =>
        {
            options.User.RequireUniqueEmail = true;
        })
        .AddEntityFrameworkStores<AuthContext>()
        .AddDefaultTokenProviders();

        builder.Services.AddScoped<JwtService>();
        builder.Services.AddDbContext<AuthContext>((provider, options) =>
        {
            var defaultConn = builder.Configuration.GetConnectionString("AuthConnection");
            var connectionString = defaultConn!;
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

        builder.Services.AddIdentityCore<User>(options =>
         {
             options.Password.RequireDigit = true;
             options.Password.RequireLowercase = true;
             options.Password.RequireNonAlphanumeric = true;
             options.Password.RequireUppercase = true;
             options.Password.RequiredLength = 6;
             options.Password.RequiredUniqueChars = 1;
             options.User.RequireUniqueEmail = true;
             options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
             options.Lockout.MaxFailedAccessAttempts = 5;
             options.Lockout.AllowedForNewUsers = true;
             options.ClaimsIdentity.SecurityStampClaimType = "AspNet.Identity.SecurityStamp";
             //options.User.AllowedUserNameCharacters = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789-._@+";
         })
         .AddRoles<Domain.Entities.Role>()
         .AddEntityFrameworkStores<AuthContext>()
         .AddApiEndpoints();

        builder.Services.AddApiVersioning(
                options =>
                {
                    options.DefaultApiVersion = new ApiVersion(1.0);
                    options.AssumeDefaultVersionWhenUnspecified = true;
                    // reporting api versions will return the headers
                    // "api-supported-versions" and "api-deprecated-versions"
                    options.ReportApiVersions = true;
                    options.ApiVersionReader = ApiVersionReader.Combine(
                           new UrlSegmentApiVersionReader(),
                           new QueryStringApiVersionReader("api-version"),
                           new HeaderApiVersionReader("X-Version"),
                           new MediaTypeApiVersionReader("x-version"));
                })
            .AddMvc(
                options =>
                {
                    // automatically applies an api version based on the name of
                    // the defining controller's namespace
                    options.Conventions.Add(new VersionByNamespaceConvention());
                })
            .AddApiExplorer(setup =>
            {
                setup.GroupNameFormat = "'v'VVV";
                setup.SubstituteApiVersionInUrl = true;
            });
        builder.Services.AddAuthorizationBuilder()
        .SetFallbackPolicy(new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .Build());

        // Auth signs its own tokens with RSA (see RsaKeyProvider/JwtService.CreateTokenAsync) —
        // validate incoming requests to Auth's own API using that same key directly, no HTTP
        // round-trip needed since Auth already holds it in-process. AllowLegacyHmacValidation
        // keeps the old shared-secret path alive as a fallback during the migration window.
        builder.Services.AddAuthentication(options =>
        {
            // Default to JWT for API requests
            options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
            options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
            // IMPORTANT: This handles the temporary data Google sends back
            options.DefaultSignInScheme = IdentityConstants.ExternalScheme;
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
                OnMessageReceived = context =>
                {
                    var accessToken = context.Request.Query["access_token"];
                    var path = context.HttpContext.Request.Path;
                    if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hubs"))
                    {
                        context.Token = accessToken;
                    }
                    return Task.CompletedTask;
                },
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

                // ✅ Add this — fires when token is valid but user lacks permission
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
        })
        //.AddCookie()
        .AddGoogle(options =>
        {
            options.ClientId = builder.Configuration["Authentication:Google:ClientId"]!;
            options.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"]!;
        })
        ;

        // Resolve RsaKeyProvider lazily from the real (post-Build) app container rather than a
        // throwaway one built here: RsaKeyProvider is a stateful, disk-persisted singleton whose
        // Data Protection key ring isn't fully configured yet at this point in the method (the
        // PersistKeysToFileSystem call happens later, in Program.cs) — building a second
        // container for it here would encrypt/decrypt against a different key ring than the
        // real app uses, producing a *different* RSA keypair than the one JwtService actually
        // signs with. Configure<T> defers resolution until options are first requested, by
        // which point the real container (and its one true RsaKeyProvider singleton) exists.
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<RsaKeyProvider>((options, rsaKeyProvider) =>
            {
                var legacySigningKey = builder.Configuration["JwtSettings:SigningKey"];
                var allowLegacyHmac = builder.Configuration.GetValue<bool?>("JwtSettings:AllowLegacyHmacValidation") ?? true;

                var signingKeys = new List<SecurityKey> { rsaKeyProvider.SigningKey };
                if (allowLegacyHmac && !string.IsNullOrEmpty(legacySigningKey))
                {
                    signingKeys.Add(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(legacySigningKey)));
                }
                options.TokenValidationParameters.IssuerSigningKeys = signingKeys;
            });
    }
}
