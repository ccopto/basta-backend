using Basta.Server.Data;
using Basta.Server.Hubs;
using Basta.Server.Services;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

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
builder.Services.AddSignalR();

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

// Register Game Session Service
builder.Services.AddSingleton<IGameSessionService, GameSessionService>();

var app = builder.Build();

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

// Automatically apply pending database migrations
using (var scope = app.Services.CreateScope())
{
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
