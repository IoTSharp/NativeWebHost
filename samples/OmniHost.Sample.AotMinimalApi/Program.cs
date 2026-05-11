using System.Text.Json.Serialization;

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:5078");
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.TypeInfoResolverChain.Insert(0, SampleJsonSerializerContext.Default);
});

var app = builder.Build();
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapGet("/api/status", () =>
    Results.Json(
        new AotStatus(
            "AOT-friendly Minimal API",
            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss 'UTC'"),
            ".NET 10 NativeAOT-ready backend with static HTML front-end"),
        SampleJsonSerializerContext.Default.AotStatus));

app.MapGet("/api/todos", () =>
    Results.Json(
        new[]
        {
            new TodoItem("Trim reflection-heavy features", true),
            new TodoItem("Prefer typed JSON payloads", true),
            new TodoItem("Use this server behind OmniHost or a normal browser", false),
        },
        SampleJsonSerializerContext.Default.TodoItemArray));

app.Run();

public sealed record AotStatus(
    string Name,
    string TimestampUtc,
    string Notes);

public sealed record TodoItem(
    string Title,
    bool Complete);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AotStatus))]
[JsonSerializable(typeof(TodoItem[]))]
internal sealed partial class SampleJsonSerializerContext : JsonSerializerContext
{
}

