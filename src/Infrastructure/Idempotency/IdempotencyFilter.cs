using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using MultiVendorPlatform.Modules.Common.Models;

namespace MultiVendorPlatform.Infrastructure.Idempotency;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class IdempotentAttribute : Attribute, IAsyncActionFilter
{
    public const string HeaderName = "Idempotency-Key";

    public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
    {
        var httpContext = context.HttpContext;
        var idempotencyKey = httpContext.Request.Headers[HeaderName].FirstOrDefault();

        // If no idempotency key is provided, proceed normally
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            await next();
            return;
        }

        var idempotencyService = httpContext.RequestServices.GetRequiredService<IIdempotencyService>();

        // Read request body for payload hash
        httpContext.Request.EnableBuffering();
        string requestBody = string.Empty;
        using (var reader = new StreamReader(httpContext.Request.Body, Encoding.UTF8, leaveOpen: true))
        {
            requestBody = await reader.ReadToEndAsync();
            httpContext.Request.Body.Position = 0;
        }

        var path = httpContext.Request.Path.Value ?? string.Empty;
        var recordResult = await idempotencyService.GetRecordAsync(idempotencyKey, path, requestBody, httpContext.RequestAborted);

        if (recordResult.IsFailure)
        {
            var err = recordResult.Error;
            context.Result = new ConflictObjectResult(ApiResponse<object?>.Fail(err.Code, err.Message, httpContext.Items["CorrelationId"]?.ToString()));
            return;
        }

        var cachedRecord = recordResult.Value;
        if (cachedRecord != null)
        {
            // Return cached response directly
            var responseJson = cachedRecord.ResponseBodyJson;
            context.Result = new ContentResult
            {
                Content = responseJson,
                ContentType = "application/json",
                StatusCode = cachedRecord.ResponseStatusCode
            };
            httpContext.Response.Headers.TryAdd("X-Cache-Lookup", "HIT-IDEMPOTENT");
            return;
        }

        // Execute action
        var executedContext = await next();

        // If succeeded (or valid response status), cache it
        if (executedContext.Result is ObjectResult objectResult && objectResult.StatusCode is >= 200 and < 300)
        {
            await idempotencyService.SaveRecordAsync(
                idempotencyKey,
                path,
                requestBody,
                objectResult.StatusCode ?? 200,
                objectResult.Value ?? new { },
                TimeSpan.FromHours(24),
                httpContext.RequestAborted);
        }
    }
}
