using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Net.Http.Headers;

namespace WslcAgent.Server;

/// <summary>
/// The single-page fallback: <c>index.html</c> for every route the API does
/// not own, so a deep link into the Blazor UI loads the app. The page is
/// never cached by the browser (<c>no-cache</c>: revalidate every time), so
/// a refresh always picks up the assets of the build that is running.
/// </summary>
public static class UiFallback
{
    private const string Page = "index.html";

    public static void MapUiFallback(this WebApplication app)
    {
        // An API route the agent does not own is a 404, not the page: a client
        // newer than the agent read the page as JSON and failed with
        // "'<' is an invalid start of a value".
        // The more specific pattern wins over the page's catch-all.
        app.MapFallback("/api/{**rest}", () => Results.Problem(
            "This agent has no such API route; it may be older than the client.",
            statusCode: StatusCodes.Status404NotFound,
            title: "No such API route"));

        if (!app.Environment.IsDevelopment())
        {
            app.MapFallbackToFile(Page, new StaticFileOptions
            {
                OnPrepareResponse = context => context.Context.Response.Headers[HeaderNames.CacheControl] = "no-cache",
            });
            return;
        }

        // dotnet watch injects its browser-refresh script (the one that also
        // carries Blazor hot reload) into HTML written through the response
        // body. MapFallbackToFile sends the file with SendFile, which bypasses
        // that, so no edit ever reached the open page. Development streams the
        // page through the body instead; Production keeps SendFile.
        app.MapFallback(async context =>
        {
            var file = app.Environment.WebRootFileProvider.GetFileInfo(Page);
            if (!file.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.Headers[HeaderNames.CacheControl] = "no-cache";
            await using var stream = file.CreateReadStream();
            await stream.CopyToAsync(context.Response.Body, context.RequestAborted);
        });
    }
}
