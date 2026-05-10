using Basta.Server.Data;
using Basta.Server.Hubs;
using Basta.Server.Services;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

// Register ProblemDetails services so all error responses follow RFC 7807.
builder.Services.AddProblemDetails();

// Configure Swagger/OpenAPI
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
    {
        Title = "Basta! Online API",
        Version = "v1",
        Description = "Real-time multiplayer word game backend — SignalR + REST API"
    });
});

// Configure Entity Framework Core with SQLite
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection") 
    ?? "Data Source=basta.db";
builder.Services.AddDbContext<BastaDbContext>(options =>
    options.UseSqlite(connectionString));

// Configure SignalR
builder.Services.AddSignalR(options =>
{
    options.KeepAliveInterval = TimeSpan.FromSeconds(15);
    options.ClientTimeoutInterval = TimeSpan.FromSeconds(30);
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
});

// Configure CORS (especially for local dev against Angular)
var corsOrigins = builder.Configuration.GetValue<string>("CORS_ORIGINS")?.Split(',') 
    ?? new[] { "http://localhost:4200" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("DefaultCorsPolicy", policy =>
    {
        policy.WithOrigins(corsOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod()
              .AllowCredentials(); // Required for SignalR
    });
});

// Configure Health Checks
builder.Services.AddHealthChecks();

// Register Game Session Service (Singleton: holds in-memory game state across requests)
builder.Services.AddSingleton<IGameSessionService, GameSessionService>();

// Register Game Operation Service (Scoped: wraps a DbContext transaction per request)
builder.Services.AddScoped<IGameOperationService, GameOperationService>();

// Register Scoring Service (Scoped: performs point calculation and DB updates)
builder.Services.AddScoped<IScoringService, ScoringService>();

// Register TimeProvider for testable time-dependent logic
builder.Services.AddSingleton(TimeProvider.System);



var app = builder.Build();

// Global exception handler — returns RFC 7807 ProblemDetails JSON for any unhandled exception.
// Must be registered before all other middleware to catch errors from any point in the pipeline.
app.UseExceptionHandler(errApp =>
{
    errApp.Run(async context =>
    {
        var feature = context.Features.Get<IExceptionHandlerFeature>();
        var exception = feature?.Error;

        var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
        logger.LogError(exception, "Unhandled exception for {Method} {Path}",
            context.Request.Method, context.Request.Path);

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status500InternalServerError,
            Title = "An unexpected error occurred.",
            Detail = app.Environment.IsDevelopment() ? exception?.Message : null
        };

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsJsonAsync(problem);
    });
});

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "Basta! Online API v1");
        options.RoutePrefix = "swagger";
    });
}

// TODO: For production, replace auto-migrations with a dedicated CI/CD migration step (e.g., dotnet ef database update in the pipeline)
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<BastaDbContext>();
    db.Database.Migrate();
}

// In a containerized environment with Nginx, HTTPS redirection might be handled by the proxy
// app.UseHttpsRedirection();

app.UseCors("DefaultCorsPolicy");

app.UseAuthorization();

app.MapControllers();

// Map SignalR Hub
app.MapHub<BastaHub>("/hubs/basta");

// Map Health Check
app.MapHealthChecks("/api/health");

app.Run();
