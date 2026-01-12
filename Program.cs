using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

var builder = WebApplication.CreateBuilder(args);

// Configure server URLs from configuration (appsettings.json "Urls")
var urlsConfig = builder.Configuration["Urls"]; 
if (!string.IsNullOrWhiteSpace(urlsConfig))
{
    // Support semicolon separated list
    var urls = urlsConfig.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
    builder.WebHost.UseUrls(urls);
}

// Add services
builder.Services.AddOpenApi();

// CORS - allow frontend origins (configured via appsettings: AllowedOrigins)
builder.Services.AddCors(options =>
{
    var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
    options.AddPolicy("AllowFrontend", policy =>
    {
        if (allowedOrigins != null && allowedOrigins.Length > 0)
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyMethod()
                  .AllowAnyHeader()
                  .AllowCredentials();
        }
        else
        {
            // No configured origins: allow any origin but do not allow credentials
            policy.AllowAnyOrigin()
                  .AllowAnyMethod()
                  .AllowAnyHeader();
        }
    });
});

// Load YARP reverse proxy configuration from appsettings.json
builder.Services.AddReverseProxy().LoadFromConfig(builder.Configuration.GetSection("ReverseProxy"));

// Controllers (if any)
builder.Services.AddControllers();

var app = builder.Build();

// Serve SPA static files from wwwroot (Vue dist should be placed here)
app.UseDefaultFiles();
app.UseStaticFiles();

//app.UseHttpsRedirection();

// Enable CORS
app.UseCors("AllowFrontend");

// Handle preflight requests for API
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/api/v1") &&
        context.Request.Method == HttpMethods.Options)
    {
        var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
        var requestOrigin = context.Request.Headers["Origin"].ToString();
        if (!string.IsNullOrEmpty(requestOrigin) && allowedOrigins != null && allowedOrigins.Contains(requestOrigin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = requestOrigin;
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
        }
        else if (allowedOrigins != null && allowedOrigins.Length > 0)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = allowedOrigins[0];
            context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
        }
        else
        {
            // fallback: allow any origin but do not allow credentials
            context.Response.Headers["Access-Control-Allow-Origin"] = "*";
        }

        context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,PUT,DELETE,OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type,Authorization";
        context.Response.StatusCode = StatusCodes.Status204NoContent;
        return;
    }
    await next();
});

// Ensure CORS response headers are present on proxied responses
app.Use(async (context, next) =>
{
    // Run the downstream handlers (including YARP)
    await next();

    if (context.Request.Path.StartsWithSegments("/api/v1"))
    {
        // Mirror Origin header if allowed, fallback to first configured origin
        var allowedOrigins = builder.Configuration.GetSection("AllowedOrigins").Get<string[]>();
        var origin = context.Request.Headers["Origin"].ToString();
        if (!string.IsNullOrEmpty(origin) && allowedOrigins != null && allowedOrigins.Contains(origin))
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = origin;
        }
        else if (allowedOrigins != null && allowedOrigins.Length > 0)
        {
            context.Response.Headers["Access-Control-Allow-Origin"] = allowedOrigins[0];
        }
        context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
        context.Response.Headers["Access-Control-Allow-Methods"] = "GET,POST,PUT,DELETE,OPTIONS";
        context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type,Authorization";
    }
});

// Map reverse proxy
app.MapReverseProxy();

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Map controllers
app.MapControllers();

// Fallback to SPA
app.MapFallbackToFile("index.html");

app.Run();
