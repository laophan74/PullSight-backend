var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>()
    ?? [];

builder.Services.AddControllers();
builder.Services.AddCors(options =>
{
    options.AddPolicy("Frontend", policy =>
    {
        if (allowedOrigins.Length > 0)
        {
            policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod();
        }
    });
});
builder.Services.AddScoped<PullSight.Api.Services.ReviewAnalysis.ICodeReviewAnalyzer, PullSight.Api.Services.ReviewAnalysis.RuleBasedCodeReviewAnalyzer>();
// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

app.UseCors("Frontend");

app.UseAuthorization();

app.MapControllers();

app.Run();
