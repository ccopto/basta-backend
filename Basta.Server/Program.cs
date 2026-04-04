using Basta.Server.Data;
using Basta.Server.Hubs;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();

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

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    // Any dev-specific configuration
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
