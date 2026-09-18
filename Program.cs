using AxiGitAPI.Services;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

// Configure Serilog
Log.Logger = new LoggerConfiguration().MinimumLevel.Information().WriteTo.File("Logs/log-.txt",rollingInterval: RollingInterval.Day,retainedFileCountLimit: 7).CreateLogger();

builder.Host.UseSerilog();

builder.Services.AddControllers();

builder.Services.AddScoped<GitPushService>();

builder.Services.AddScoped<GitPullService>();

builder.Services.AddHttpClient();

builder.Services.AddScoped<RepositoryListService>();

var app = builder.Build();

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();