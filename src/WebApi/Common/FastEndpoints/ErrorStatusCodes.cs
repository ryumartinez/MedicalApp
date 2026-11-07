using FluentValidation.Results;

namespace MedicalApp.Common.FastEndpoints;

public static class ErrorStatusCodes
{
    public static int ToStatusCode(this Error error) => error.Type switch
    {
        ErrorType.NotFound => StatusCodes.Status404NotFound,
        ErrorType.Conflict => StatusCodes.Status409Conflict,
        ErrorType.Forbidden => StatusCodes.Status403Forbidden,
        ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
        _ => StatusCodes.Status400BadRequest
    };

    /// <summary>
    /// Sends domain errors as problem details. The first error decides the status code.
    /// </summary>
    public static Task SendErrorsAsync(this HttpContext context, IReadOnlyList<Error> errors, CancellationToken cancellationToken) =>
        context.Response.SendErrorsAsync(
            errors.Select(error => new ValidationFailure(error.Code, error.Description) { ErrorCode = error.Code }).ToList(),
            errors[0].ToStatusCode(),
            cancellation: cancellationToken);
}
