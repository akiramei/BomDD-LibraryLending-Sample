namespace Library.Api.Middleware;

/// <summary>
/// Error handling middleware to prevent unhandled exceptions from returning 500
/// Per work order: prevent 500 errors from unhandled exceptions
/// </summary>
public class ErrorHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ErrorHandlingMiddleware> _logger;

    public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");

            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.ContentType = "application/json";

            var response = new
            {
                error = new
                {
                    code = "invalid_request",
                    message = "An error occurred processing your request"
                }
            };

            await context.Response.WriteAsJsonAsync(response);
        }
    }
}
