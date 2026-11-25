using FaizaGold.Domain;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FaizaGold.Api.Infrastructure;

/// <summary>Превращает исключения в понятные ответы ProblemDetails с текстом на русском.</summary>
public sealed class ErrorHandler(ILogger<ErrorHandler> logger) : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext http, Exception ex, CancellationToken ct)
    {
        var (status, message) = ex switch
        {
            DomainException or ValidationException => (StatusCodes.Status400BadRequest, ex.Message),
            ForbiddenException => (StatusCodes.Status403Forbidden, ex.Message),
            NotFoundException => (StatusCodes.Status404NotFound, ex.Message),
            DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
                (StatusCodes.Status409Conflict, "Такая запись уже существует или операция уже выполнена."),
            BadHttpRequestException bad => (bad.StatusCode, "Некорректный запрос."),
            _ => (StatusCodes.Status500InternalServerError, "Внутренняя ошибка сервера. Попробуйте ещё раз."),
        };
        if (status >= 500)
            logger.LogError(ex, "Unhandled error on {Path}", http.Request.Path);

        http.Response.StatusCode = status;
        await http.Response.WriteAsJsonAsync(new { title = message, status }, ct);
        return true;
    }
}

public sealed class ValidationException(string message) : Exception(message);

public static class Check
{
    public static string Text(string? value, string field, int max, bool required = true)
    {
        var v = value?.Trim() ?? "";
        if (required && v.Length == 0) throw new ValidationException($"Заполните поле «{field}».");
        if (v.Length > max) throw new ValidationException($"Поле «{field}» длиннее {max} символов.");
        return v;
    }

    public static string? Optional(string? value, string field, int max)
    {
        var v = Text(value, field, max, required: false);
        return v.Length == 0 ? null : v;
    }

    public static string Password(string? value)
    {
        if (value is null || value.Length < 8)
            throw new ValidationException("Пароль должен быть не короче 8 символов.");
        if (value.Length > 128)
            throw new ValidationException("Пароль слишком длинный.");
        if (!value.Any(char.IsLetter) || !value.Any(char.IsDigit))
            throw new ValidationException("Пароль должен содержать буквы и цифры.");
        return value;
    }

    public static T Enum<T>(T value, string field) where T : struct, System.Enum =>
        System.Enum.IsDefined(value) ? value : throw new ValidationException($"Неверное значение поля «{field}».");
}
