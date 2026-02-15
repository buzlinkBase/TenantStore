using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using OnePunch.Notification.Core.Messaging;
using OnePunch.Notification.Domain.DTO;
using System.Text;

namespace OnePunch.Notification;
public static class ServiceRegistrations
{
    public static void RegisterSelftServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddHttpContextAccessor(); 
        builder.Services.AddDataProtection(); 
        builder.Services.AddLogging();
        builder.Services.AddSingleton<ProducerService>();
        builder.Services.AddHostedService<UserCreatedWorker>();

        builder.Services.Configure<KafkaSettings>(builder.Configuration.GetSection("KafkaSettings"));
        builder.Services.Configure<RouteOptions>(options => { options.LowercaseUrls = true; });
        builder.Services.Configure<HMacSetting>(builder.Configuration.GetSection("HMacSettings"));
        builder.Services.Configure<CryptoSetting>(builder.Configuration.GetSection("Crypto"));
        builder.Services.Configure<MailSettings>(builder.Configuration.GetSection("MailSettings"));
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAll", policy =>
            {
                policy.AllowAnyOrigin()
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

        // Swagger (defer versioned docs to Program.cs)
        builder.Services.AddEndpointsApiExplorer();
        //builder.Services.AddSwaggerGen(options =>
        //{
    //        options.SwaggerDoc("v1", new OpenApiInfo
    //        {
    //            Title = "OnePunch c API",
    //            Version = "v1"
    //        });

    //        // JWT Bearer
    //        options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    //        {
    //            Name = "Authorization",
    //            Type = SecuritySchemeType.ApiKey,
    //            Scheme = "Bearer",
    //            BearerFormat = "JWT",
    //            In = ParameterLocation.Header,
    //            Description = "Enter 'Bearer' [space] and then your valid JWT token.\nExample: \"Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6...\""
    //        });

    //        // API Key
    //        options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    //        {
    //            Description = "API Key needed to access the endpoints. Example: \"X-Api-Key: {key}\"",
    //            Name = "X-Api-Key",
    //            In = ParameterLocation.Header,
    //            Type = SecuritySchemeType.ApiKey,
    //            Scheme = "ApiKeyScheme"
    //        });

    //        // Apply both globally
    //        options.AddSecurityRequirement(new OpenApiSecurityRequirement
    //{
    //    {
    //        new OpenApiSecurityScheme
    //        {
    //            Reference = new OpenApiReference
    //            {
    //                Type = ReferenceType.SecurityScheme,
    //                Id = "Bearer"
    //            }
    //        },
    //        Array.Empty<string>()
    //    },
    //    {
    //        new OpenApiSecurityScheme
    //        {
    //            Reference = new OpenApiReference
    //            {
    //                Type = ReferenceType.SecurityScheme,
    //                Id = "ApiKey"
    //            }
    //        },
    //        Array.Empty<string>()
    //    } });
        //});

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
                 ValidateIssuerSigningKey = true,
                 IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(builder.Configuration["JwtSettings:SigningKey"]!))
             };
         });
    }
}
