var builder = WebApplication.CreateBuilder(args);

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins("http://localhost:5173")
              .AllowAnyHeader()
              .AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();

app.MapGet("/", () => "ChatApp API is running.");

app.MapPost("/chat", (ChatRequest req) =>
{
    var reply = $"You said: {req.Message}";
    return Results.Ok(new { reply });
});

app.Run();

record ChatRequest(string Message);
