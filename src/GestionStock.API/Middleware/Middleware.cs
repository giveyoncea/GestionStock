using System.Net;
using System.Text.Json;

namespace GestionStock.API.Middleware;

/// <summary>
/// Middleware de gestion centralisée des exceptions.
/// Retourne des réponses JSON uniformes pour toutes les erreurs.
/// </summary>
public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next,
        ILogger<ExceptionHandlingMiddleware> logger)
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
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (statusCode, message) = exception switch
        {
            ArgumentException => (HttpStatusCode.BadRequest, exception.Message),
            InvalidOperationException => (HttpStatusCode.Conflict, exception.Message),
            UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "Accès non autorisé."),
            KeyNotFoundException => (HttpStatusCode.NotFound, "Ressource introuvable."),
            _ => (HttpStatusCode.InternalServerError, "Une erreur interne est survenue.")
        };

        if (statusCode == HttpStatusCode.InternalServerError)
            _logger.LogError(exception, "Erreur non gérée : {Message}", exception.Message);
        else
            _logger.LogWarning("Erreur applicative ({Code}) : {Message}",
                statusCode, exception.Message);

        context.Response.ContentType = "application/json";
        context.Response.StatusCode = (int)statusCode;

        var response = new
        {
            succes = false,
            message,
            code = (int)statusCode,
            timestamp = DateTime.UtcNow
        };

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(response,
                new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }));
    }
}

/// <summary>
/// Middleware d'audit automatique – enregistre l'IP pour le journal d'audit.
/// </summary>
public class AuditMiddleware
{
    private readonly RequestDelegate _next;

    public AuditMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context)
    {
        // Rendre l'IP disponible dans les services via HttpContext
        var ip = context.Connection.RemoteIpAddress?.ToString()
              ?? context.Request.Headers["X-Forwarded-For"].FirstOrDefault()
              ?? "unknown";

        context.Items["ClientIP"] = ip;
        await _next(context);
    }
}
