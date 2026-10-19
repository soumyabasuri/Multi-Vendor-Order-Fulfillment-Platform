using Microsoft.AspNetCore.Mvc;
using MultiVendorPlatform.Modules.Common.Models;
using MultiVendorPlatform.Modules.Common.Results;

namespace MultiVendorPlatform.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public abstract class BaseApiController : ControllerBase
{
    protected string? CorrelationId => HttpContext.Items["CorrelationId"]?.ToString();
    protected string? ClientIpAddress => HttpContext.Connection.RemoteIpAddress?.ToString();

    protected IActionResult HandleResult<T>(Result<T> result)
    {
        if (result.IsSuccess)
        {
            return Ok(ApiResponse<T>.Ok(result.Value, CorrelationId));
        }

        return MapError(result.Error);
    }

    protected IActionResult HandleResult(Result result)
    {
        if (result.IsSuccess)
        {
            return Ok(ApiResponse<object?>.Ok(null, CorrelationId));
        }

        return MapError(result.Error);
    }

    private IActionResult MapError(Error error)
    {
        var response = ApiResponse<object?>.Fail(error.Code, error.Message, CorrelationId);

        return error.Type switch
        {
            ErrorType.NotFound => NotFound(response),
            ErrorType.Validation => BadRequest(response),
            ErrorType.Conflict => Conflict(response),
            ErrorType.Unauthorized => Unauthorized(response),
            ErrorType.Forbidden => StatusCode(StatusCodes.Status403Forbidden, response),
            _ => StatusCode(StatusCodes.Status500InternalServerError, response)
        };
    }

    protected IActionResult Forbidden(string message, string errorCode = "Forbidden")
    {
        var response = ApiResponse<object?>.Fail(errorCode, message, CorrelationId);
        return StatusCode(StatusCodes.Status403Forbidden, response);
    }
}
