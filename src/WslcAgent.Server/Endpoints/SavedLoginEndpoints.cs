using Microsoft.AspNetCore.Http.HttpResults;
using WslcAgent.ApiClient.Contracts;
using WslcAgent.Server.SavedLogins;

namespace WslcAgent.Server.Endpoints;

/// <summary>
/// <c>/api/v1/saved-logins</c>: the logins kept for the host browser pane, edited in
/// Settings. No MCP tool on purpose: the passwords are for the person at the pane,
/// not for an AI agent. See docs/api-v1.md.
/// </summary>
public static class SavedLoginEndpoints
{
    public static RouteGroupBuilder MapSavedLoginEndpoints(this RouteGroupBuilder api)
    {
        api.MapGet("/saved-logins", (SavedLoginStore store) => store.List())
            .WithName("ListSavedLogins");

        api.MapPost("/saved-logins", Results<Created<SavedLogin>, ProblemHttpResult> (SavedLoginInput input, SavedLoginStore store) =>
            {
                try
                {
                    var created = store.Add(input);
                    return TypedResults.Created($"/api/v1/saved-logins/{created.Id}", created);
                }
                catch (Exception ex) when (Problem(ex) is { } problem)
                {
                    return problem;
                }
            })
            .WithName("CreateSavedLogin");

        api.MapPut("/saved-logins/{id}", Results<Ok<SavedLogin>, NotFound, ProblemHttpResult> (string id, SavedLoginInput input, SavedLoginStore store) =>
            {
                try
                {
                    return store.Update(id, input) is { } updated ? TypedResults.Ok(updated) : TypedResults.NotFound();
                }
                catch (Exception ex) when (Problem(ex) is { } problem)
                {
                    return problem;
                }
            })
            .WithName("UpdateSavedLogin");

        api.MapDelete("/saved-logins/{id}", Results<NoContent, NotFound> (string id, SavedLoginStore store) =>
                store.Remove(id) ? TypedResults.NoContent() : TypedResults.NotFound())
            .WithName("DeleteSavedLogin");

        return api;
    }

    /// <summary>A missing title is a 400 and a title already used a 409; anything else is not the caller's to fix.</summary>
    private static ProblemHttpResult? Problem(Exception ex) => ex switch
    {
        SavedLoginConflictException => TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status409Conflict, title: "Title already used"),
        ArgumentException => TypedResults.Problem(ex.Message, statusCode: StatusCodes.Status400BadRequest, title: "Invalid saved login"),
        _ => null,
    };
}
