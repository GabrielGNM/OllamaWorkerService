using System.Text.Json.Serialization;

namespace OllamaWorkerService.Models;

public record ChatRequest
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("messages")]
    public List<Message> Messages { get; init; } = new();

    [JsonPropertyName("tools")]
    public List<Tool>? Tools { get; init; }

    [JsonPropertyName("stream")]
    public bool Stream { get; init; } = false;
}

public record Message
{
    [JsonPropertyName("role")]
    public string Role { get; init; } = string.Empty;

    [JsonPropertyName("content")]
    public string Content { get; init; } = string.Empty;

    [JsonPropertyName("tool_calls")]
    public List<ToolCall>? ToolCalls { get; init; }
}

public record Tool
{
    [JsonPropertyName("type")]
    public string Type { get; init; } = "function";

    [JsonPropertyName("function")]
    public FunctionDefinition Function { get; init; } = new();
}

public record FunctionDefinition
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; init; } = string.Empty;

    [JsonPropertyName("parameters")]
    public object Parameters { get; init; } = new();
}

public record ChatResponse
{
    [JsonPropertyName("model")]
    public string Model { get; init; } = string.Empty;

    [JsonPropertyName("message")]
    public Message? Message { get; init; }

    [JsonPropertyName("done")]
    public bool Done { get; init; }
}

public record ToolCall
{
    [JsonPropertyName("function")]
    public FunctionCall Function { get; init; } = new();
}

public record FunctionCall
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = string.Empty;

    [JsonPropertyName("arguments")]
    public Dictionary<string, object> Arguments { get; init; } = new();
}
