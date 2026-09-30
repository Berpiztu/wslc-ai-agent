using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using WslcAgent.Server.Wslc;

namespace WslcAgent.Server.Endpoints;

/// <summary>
/// Turns the exceptions the services throw into problem details the UI and the
/// tools show verbatim — and writes them to the log as errors: what the
/// application shows in red has to be in Logs under Error.
/// A failed <c>wslc</c> command is already there, as the command's own entry,
/// so its exception is not written twice: neither by this handler nor by the
/// middleware around it, which would otherwise log every one of them as an
/// unhandled exception, stack trace and all. A session being stopped fails
/// every command that was in flight, and that was five stack traces for one
/// deliberate act.
/// </summary>
public static class ProblemMapping
{
    public static IApplicationBuilder UseWslcProblemDetails(this IApplicationBuilder app) =>
        app.UseExceptionHandler(new ExceptionHandlerOptions
        {
            SuppressDiagnosticsCallback = context => IsCommandFailure(context.Exception),
            ExceptionHandler = async context =>
            {
                var error = context.Features.Get<IExceptionHandlerFeature>()?.Error;
                var (status, title) = error switch
                {
                    WslcNotFoundException => (StatusCodes.Status503ServiceUnavailable, "wslc is not available"),
                    WslcException => (StatusCodes.Status502BadGateway, "wslc command failed"),
                    TimeoutException => (StatusCodes.Status504GatewayTimeout, "wslc command timed out"),
                    ArgumentException => (StatusCodes.Status400BadRequest, "Invalid request"),
                    KeyNotFoundException => (StatusCodes.Status404NotFound, "Not found"),
                    InvalidOperationException => (StatusCodes.Status409Conflict, "Not possible now"),
                    _ => (StatusCodes.Status500InternalServerError, "Unexpected error"),
                };
                if (!IsCommandFailure(error))
                {
                    context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ProblemMapping))
                        .LogError("{Method} {Path} answered {Status} {Title}: {Detail}", context.Request.Method, context.Request.Path, status, title, error?.Message);
                }

                context.Response.StatusCode = status;
                var problem = new ProblemDetails { Status = status, Title = title, Detail = error?.Message };
                // A failed launch says which fields of the form it is about: the form shows the reason in them.
                if (error is WslcException { Fields.Count: > 0 } launch)
                {
                    problem.Extensions["fields"] = launch.Fields;
                }

                await WriteAsync(context.Response, problem);
            },
        });

    /// <summary>
    /// A refusal of the agent's own, as problem details and said to be so. The
    /// type is what tells the client the agent answered: a 502, 503 or 504 of
    /// any other type is taken for a gateway answering for an agent that is not
    /// there, and the screen waits behind "Waiting for the agent…". Written as
    /// plain JSON, the agent's own 502 — a session the user stopped, refusing
    /// the command that would open it again — put every other client in a loop
    /// of that screen and a reload.
    /// </summary>
    public static Task WriteAsync(HttpResponse response, ProblemDetails problem) =>
        response.WriteAsJsonAsync(problem, options: null, contentType: "application/problem+json");

    /// <summary>The <c>wslc</c> command itself failed, and CLI Activity has already written it with its output.</summary>
    private static bool IsCommandFailure(Exception? error) => error is WslcException or TimeoutException;
}
