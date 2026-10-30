using AxiGitAPI.Options;
using AxiGitAPI.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Machine-local secrets such as the GitHub PAT (git-ignored).
// Environment variables and command-line args are re-added after it
// so they keep overriding JSON settings, as CreateBuilder intends.
builder.Configuration
    .AddJsonFile("appsettings.Local.json", optional: true)
    .AddEnvironmentVariables()
    .AddCommandLine(args);

builder.Services.Configure<GitHubOptions>(
    builder.Configuration.GetSection(GitHubOptions.SectionName));

// Configure Serilog
Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.File("Logs/log-.txt",rollingInterval: RollingInterval.Day,retainedFileCountLimit: 7).CreateLogger();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy
            .AllowAnyOrigin()
            .AllowAnyMethod()
            .AllowAnyHeader();
    });
});

builder.Host.UseSerilog();

builder.Services.AddControllers();

builder.Services.AddScoped<GitPushService>();

builder.Services.AddScoped<GitPullService>();

builder.Services.AddHttpClient();

builder.Services.AddScoped<RepositoryListService>();

var app = builder.Build();

app.UseCors("AllowAll");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();