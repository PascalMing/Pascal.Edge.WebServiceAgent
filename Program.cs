using System.Net.Http.Headers;
using Pascal.Edge.WebServiceAgent.Models;
using Pascal.Edge.WebServiceAgent.Services;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSingleton<SiteConfigurationLoader>();

var app = builder.Build();

var configLoader = app.Services.GetRequiredService<SiteConfigurationLoader>();
var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };

app.Use(next =>
{
    return async context =>
    {
        var host = context.Request.Host.Host;
        var options = configLoader.CurrentValue;
        var site = configLoader.GetSiteByHostname(host);
        
        if (site == null)
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync($"Site not found for hostname: {host}");
            return;
        }

        if (!string.IsNullOrEmpty(site.ForwardUrl))
        {
            await ForwardRequest(context, site.ForwardUrl, httpClient);
            return;
        }

        var basePath = AppContext.BaseDirectory;
        var fullPath = Path.GetFullPath(Path.Combine(basePath, site.Path));

        if (!Directory.Exists(fullPath))
        {
            context.Response.StatusCode = 404;
            await context.Response.WriteAsync($"Site directory not found: {fullPath}");
            return;
        }

        var defaultDoc = options.DefaultDocument ?? "index.html";
        var requestPath = context.Request.Path.Value?.TrimStart('/') ?? "";
        
        if (string.IsNullOrEmpty(requestPath))
        {
            requestPath = defaultDoc;
        }

        var requestedFilePath = Path.Combine(fullPath, requestPath);

        if (Directory.Exists(requestedFilePath))
        {
            requestedFilePath = Path.Combine(requestedFilePath, defaultDoc);
        }

        if (File.Exists(requestedFilePath))
        {
            context.Response.ContentType = GetContentType(requestedFilePath);
            await context.Response.SendFileAsync(requestedFilePath);
            return;
        }

        if (options.EnableSPAFallback && !Path.HasExtension(requestPath))
        {
            var spaIndexPath = Path.Combine(fullPath, defaultDoc);
            if (File.Exists(spaIndexPath))
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                await context.Response.SendFileAsync(spaIndexPath);
                return;
            }
        }

        context.Response.StatusCode = 404;
        await context.Response.WriteAsync("404 - File Not Found");
    };
});

var options = configLoader.CurrentValue;

if (options.Sites == null || options.Sites.Count == 0)
{
    Console.WriteLine("警告: 未配置任何站点，请检查 appsettings.json");
    app.Run();
    return;
}

var urls = options.Sites.Select(s => $"http://0.0.0.0:{s.Port}").ToArray();
builder.WebHost.UseUrls(urls);

Console.WriteLine($"启动站点托管服务，监听端口: {string.Join(", ", options.Sites.Select(s => s.Port))}");

foreach (var site in options.Sites)
{
    var target = !string.IsNullOrEmpty(site.ForwardUrl) 
        ? site.ForwardUrl 
        : $"./www-dist/{site.Name}";
    Console.WriteLine($"  - {site.Name}: {string.Join(", ", site.Hostnames)} -> {target}");
}

app.Run();

static async Task ForwardRequest(HttpContext context, string forwardUrl, HttpClient httpClient)
{
    try
    {
        var targetUri = new Uri(forwardUrl);
        var requestPath = context.Request.Path.Value ?? "/";
        var queryString = context.Request.QueryString.Value ?? "";
        
        var basePath = targetUri.AbsolutePath.TrimEnd('/');
        var reqPath = requestPath.TrimStart('/');
        var targetPath = $"/{reqPath}{queryString}";
        
        var targetUrl = $"{targetUri.Scheme}://{targetUri.Host}:{targetUri.Port}{basePath}{targetPath}";

        using var request = new HttpRequestMessage(new HttpMethod(context.Request.Method), targetUrl);
        
        foreach (var header in context.Request.Headers)
        {
            if (!IsExcludedHeader(header.Key))
            {
                try 
                { 
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value.ToString()); 
                } 
                catch { }
            }
        }

        if (context.Request.ContentLength > 0)
        {
            request.Content = new StreamContent(context.Request.Body);
            if (!string.IsNullOrEmpty(context.Request.ContentType))
            {
                request.Content.Headers.ContentType = MediaTypeHeaderValue.Parse(context.Request.ContentType);
            }
        }

        using var response = await httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
        
        context.Response.StatusCode = (int)response.StatusCode;
        
        foreach (var header in response.Headers.Concat(response.Content.Headers))
        {
            if (!IsExcludedResponseHeader(header.Key))
            {
                try { context.Response.Headers[header.Key] = header.Value.ToArray(); } catch { }
            }
        }

        await response.Content.CopyToAsync(context.Response.Body);
    }
    catch (TaskCanceledException)
    {
        context.Response.StatusCode = 504;
        await context.Response.WriteAsync("Gateway Timeout");
    }
    catch (HttpRequestException ex)
    {
        context.Response.StatusCode = 502;
        await context.Response.WriteAsync($"Forward error: {ex.Message}");
    }
    catch (Exception ex)
    {
        context.Response.StatusCode = 502;
        await context.Response.WriteAsync($"Forward error: {ex.Message}");
    }
}

static bool IsExcludedHeader(string headerName)
{
    var excluded = new[] { "Host", "Content-Length", "Connection", "Transfer-Encoding", "Keep-Alive" };
    return excluded.Any(e => e.Equals(headerName, StringComparison.OrdinalIgnoreCase));
}

static bool IsExcludedResponseHeader(string headerName)
{
    var excluded = new[] { "Transfer-Encoding", "Connection", "Keep-Alive", "Content-Length" };
    return excluded.Any(e => e.Equals(headerName, StringComparison.OrdinalIgnoreCase));
}

static string GetContentType(string filePath)
{
    var extension = Path.GetExtension(filePath).ToLowerInvariant();
    return extension switch
    {
        ".html" => "text/html; charset=utf-8",
        ".htm" => "text/html; charset=utf-8",
        ".css" => "text/css; charset=utf-8",
        ".js" => "application/javascript; charset=utf-8",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".gif" => "image/gif",
        ".svg" => "image/svg+xml",
        ".ico" => "image/x-icon",
        ".woff" => "font/woff",
        ".woff2" => "font/woff2",
        ".ttf" => "font/ttf",
        ".eot" => "application/vnd.ms-fontobject",
        ".otf" => "font/otf",
        ".webp" => "image/webp",
        ".webm" => "video/webm",
        ".mp4" => "video/mp4",
        ".pdf" => "application/pdf",
        ".zip" => "application/zip",
        _ => "application/octet-stream"
    };
}
