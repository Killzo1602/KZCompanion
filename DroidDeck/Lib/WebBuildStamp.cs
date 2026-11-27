using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;

namespace DroidDeck.Lib
{
    public static class WebBuildStamp
    {
        public const string Prefix = "/v/";
        public const string Placeholder = "__WEB_BUILD__";

        public static IApplicationBuilder UseWebBuildStamp(this IApplicationBuilder app, IWebHostEnvironment env)
        {
            return app.Use(async (ctx, next) =>
            {
                var path = ctx.Request.Path.Value ?? "";
                if (path.StartsWith(Prefix, StringComparison.Ordinal))
                {
                    int slash = path.IndexOf('/', Prefix.Length);
                    if (slash > 0) ctx.Request.Path = path[slash..];
                }
                else if (path is "/" or "/index.html" && HttpMethods.IsGet(ctx.Request.Method))
                {
                    var index = env.WebRootFileProvider.GetFileInfo("index.html");
                    if (index.Exists && index.PhysicalPath != null)
                    {
                        var html = await File.ReadAllTextAsync(index.PhysicalPath);
                        ctx.Response.ContentType = "text/html; charset=utf-8";
                        ctx.Response.Headers.CacheControl = "no-cache";
                        await ctx.Response.WriteAsync(html.Replace(Placeholder, Stamp(env)));
                        return;
                    }
                }
                await next();
            });
        }

        public static string Stamp(IWebHostEnvironment env)
        {
            var js = env.WebRootFileProvider.GetFileInfo("main.dart.js");
            return js.Exists ? js.LastModified.UtcTicks.ToString("x") : "dev";
        }
    }
}
