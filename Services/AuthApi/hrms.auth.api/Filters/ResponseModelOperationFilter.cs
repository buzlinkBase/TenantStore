using Microsoft.OpenApi.Any;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace OnePunch.Auth.Api.Filters;

public class ResponseModelOperationFilter : IOperationFilter
{
    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        foreach (var (statusKey, response) in operation.Responses)
        {
            if (!response.Content.TryGetValue("application/json", out var mediaType) || mediaType.Schema == null)
                continue;

            int.TryParse(statusKey, out var statusCode);
            mediaType.Schema = BuildWrappedSchema(mediaType.Schema, statusCode);
        }

        if (!operation.Responses.ContainsKey("500"))
        {
            operation.Responses["500"] = new OpenApiResponse
            {
                Description = "Internal Server Error",
                Content = new Dictionary<string, OpenApiMediaType>
                {
                    ["application/json"] = new OpenApiMediaType
                    {
                        Schema = BuildWrappedSchema(BuildProblemDetailsSchema(), 500)
                    }
                }
            };
        }
    }

    private static OpenApiSchema BuildWrappedSchema(OpenApiSchema innerSchema, int statusCode)
    {
        var isSuccess = statusCode is 0 or < 400;
        return new OpenApiSchema
        {
            Type = "object",
            Properties = new Dictionary<string, OpenApiSchema>
            {
                ["message"] = new OpenApiSchema
                {
                    Type = "string",
                    Example = new OpenApiString(isSuccess ? "Success" : "Error")
                },
                ["status"] = new OpenApiSchema
                {
                    Type = "integer",
                    Format = "int32",
                    Example = new OpenApiInteger(statusCode > 0 ? statusCode : 200)
                },
                ["data"] = innerSchema
            }
        };
    }

    private static OpenApiSchema BuildProblemDetailsSchema() => new()
    {
        Type = "object",
        Description = "ProblemDetails",
        Properties = new Dictionary<string, OpenApiSchema>
        {
            ["type"] = new OpenApiSchema { Type = "string" },
            ["title"] = new OpenApiSchema { Type = "string" },
            ["status"] = new OpenApiSchema { Type = "integer", Format = "int32" },
            ["detail"] = new OpenApiSchema { Type = "string" },
            ["instance"] = new OpenApiSchema { Type = "string" }
        }
    };
}
