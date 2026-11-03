using System.Net.Http.Headers;
using AxiGitAPI.Infrastructure;
using AxiGitAPI.Options;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
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

// Configure Serilog: console (visible during `dotnet run`) and daily rolling files.
Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.Console().WriteTo.File("Logs/log-.txt",rollingInterval: RollingInterval.Day,retainedFileCountLimit: 7).CreateLogger();

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

// GitHub REST client for ListDirectories, authenticated with the configured PAT.
builder.Services.AddHttpClient(
    DirectoryListService.GitHubClientName,
    (serviceProvider, client) =>
    {
        GitHubOptions gitHub =
            serviceProvider.GetRequiredService<IOptions<GitHubOptions>>().Value;

        client.BaseAddress = new Uri("https://api.github.com/");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("AxiGitAPI/1.0");
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", gitHub.Token);
    });

builder.Services.AddScoped<DirectoryListService>();

// Unhandled exceptions become ProblemDetails responses (see GlobalExceptionHandler).
builder.Services.AddProblemDetails();

builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

// Swagger / OpenAPI, described from the controllers and their XML doc comments.
builder.Services.AddEndpointsApiExplorer();

builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "AxiGIT API",
        Version = "v1",
        Description = "Publishes and retrieves Axi deliverables stored in GitHub Repositories for AxiCmdLine."
    });

    string xmlDocPath =
        Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");

    if (File.Exists(xmlDocPath))
        options.IncludeXmlComments(xmlDocPath);
});

var app = builder.Build();

app.UseExceptionHandler();

// Only in Development: the API description is not exposed by published builds.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();

    app.UseSwaggerUI(options =>
    {
        options.SwaggerEndpoint("/swagger/v1/swagger.json", "AxiGIT API v1");
    });
}

app.UseCors("AllowAll");

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();

// Exposes Program to WebApplicationFactory in the test project.
public partial class Program { }
