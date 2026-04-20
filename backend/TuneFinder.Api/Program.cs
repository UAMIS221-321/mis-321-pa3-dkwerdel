using TuneFinder.Api.Options;
using TuneFinder.Api.Services;
using TuneFinder.Api.Services.Interfaces;
using TuneFinder.Api.Services.Llm;
using TuneFinder.Api.Services.Rag;
using TuneFinder.Api.Services.Tools;
using TuneFinder.Api.Utils;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddUserSecrets<Program>(optional: true);

builder.Services.Configure<AiOptions>(builder.Configuration.GetSection("Ai"));

builder.Services.AddSingleton(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    return ConnectionStringResolver.ResolveMySqlConnectionString(config);
});

builder.Services.AddHttpClient<ILLMService, OpenAiCompatibleLlmService>();
builder.Services.AddScoped<IRagService, RagService>();
builder.Services.AddScoped<IToolService, ToolService>();
builder.Services.AddScoped<IChatOrchestratorService, ChatOrchestratorService>();
builder.Services.AddScoped<ISeedDataService, SeedDataService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.AddCors(options =>
{
    options.AddPolicy("FrontendPolicy", policy =>
    {
        policy
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowAnyOrigin();
    });
});

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}
else
{
    // Keep local file:// frontend usage simple during development.
    app.UseHttpsRedirection();
}
app.UseCors("FrontendPolicy");
app.MapControllers();

using (var scope = app.Services.CreateScope())
{
    var seedDataService = scope.ServiceProvider.GetRequiredService<ISeedDataService>();
    await seedDataService.InitializeDatabaseAsync();
}

app.Run();
