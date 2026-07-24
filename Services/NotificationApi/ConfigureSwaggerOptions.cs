using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OnePunch.Notification;

public class ConfigureSwaggerOptions : IConfigureOptions<SwaggerGenOptions>
{
    private readonly IApiVersionDescriptionProvider _provider;
    public ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider) => _provider = provider;
    public void Configure(SwaggerGenOptions options)
    {
        foreach (var description in _provider.ApiVersionDescriptions)
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "OnePunch Noticiation API",
                Version = "v1"
            });

            // JWT Bearer
            options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.ApiKey,
                Scheme = "Bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "Enter 'Bearer' [space] and then your valid JWT token.\nExample: \"Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6...\""
            });

            // API Key
            options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
            {
                Description = "API Key needed to access the endpoints. Example: \"X-Api-Key: {key}\"",
                Name = "X-Api-Key",
                In = ParameterLocation.Header,
                Type = SecuritySchemeType.ApiKey,
                Scheme = "ApiKeyScheme"
            });

            // Apply both globally
            options.AddSecurityRequirement(new OpenApiSecurityRequirement
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
                            Array.Empty<string>()
                        },
                        {
                            new OpenApiSecurityScheme
                            {
                                Reference = new OpenApiReference
                                {
                                    Type = ReferenceType.SecurityScheme,
                                    Id = "ApiKey"
                                }
                            },
                            Array.Empty<string>()
                        }
                });
        }
    }
}