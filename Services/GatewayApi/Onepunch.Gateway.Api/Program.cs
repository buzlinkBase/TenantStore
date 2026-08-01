using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Onepunch.Common.Lib.Cache;
using Onepunch.Common.Lib.Security;
using Onepunch.Gateway.Api.Middlewares;
using Onepunch.Gateway.Api.Protos;
using Onepunch.Gateway.Api.Services;
using Serilog;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

Log.Logger = new LoggerConfiguration()
    .ReadFrom.Configuration(builder.Configuration)
    .CreateLogger();
builder.Host.UseSerilog();

builder.Services.AddReverseProxy()
    .LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(builder.Configuration.GetConnectionString("Redis")!));
builder.Services.AddScoped<ICacheService, RedisCacheService>();

builder.Services.AddGrpcClient<GetTenantService.GetTenantServiceClient>(options =>
{
    var tenantUrl = builder.Configuration["Domains:TenantUrl"] ?? "";
    options.Address = new Uri(tenantUrl);
});

builder.Services.AddScoped<GatewayMembershipClient>();
builder.Services.AddScoped<TenantAuthorizationMiddleware>();

builder.Services.AddHttpClient<JwksClient>(client =>
{
    var authUrl = builder.Configuration["AuthUrl"] ?? "";
    client.BaseAddress = new Uri(authUrl);
});
// JwtBearerOptions is configured before the DI container is built, but the resolver needs
// JwksClient's HttpClient-factory machinery, which is already registered above — build a
// throwaway provider to fetch it (same pattern used in Tenant Service's RegisterSelfServices).
var jwksClient = builder.Services.BuildServiceProvider().GetRequiredService<JwksClient>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["JwtSettings:Issuer"] ?? "Onepunch",
            ValidateAudience = true,
            ValidAudience = builder.Configuration["JwtSettings:Audience"] ?? "Onepunch.AuthService",
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeyResolver = (token, securityToken, kid, validationParameters) =>
                jwksClient.ResolveSigningKey(token, securityToken, kid, validationParameters)
        };
    });

// No blocking fallback policy here: the Gateway proxies both public endpoints (login, signup,
// jwks) and protected ones. Downstream services still enforce their own real authentication —
// UseAuthentication() below just populates context.User when a bearer token IS present, so
// TenantAuthorizationMiddleware can layer its defense-in-depth role check on top for requests
// that are already authenticated, without blocking anonymous requests at this layer.
builder.Services.AddAuthorization();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.WithOrigins(
                "http://localhost:5173",
                "http://localhost:4200",
                "https://hris.onepunch.site",
                "https://hris-dev.onepunch.site",
                "https://hris-staging.onepunch.site",
                "https://api.onepunch.site")
            .AllowCredentials()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseSerilogRequestLogging();
app.UseCors("AllowAll");
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<TenantAuthorizationMiddleware>();
app.MapReverseProxy();

app.Run();
