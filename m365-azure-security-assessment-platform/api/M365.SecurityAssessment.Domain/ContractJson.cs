using System.Text.Json;
using System.Text.Json.Serialization;

namespace M365.SecurityAssessment.Domain;

public static class ContractJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    static ContractJson() => Options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false));

    public static T Parse<T>(string json) where T : class
    {
        try
        {
            var document = JsonSerializer.Deserialize<T>(json, Options)
                ?? throw new ContractValidationException($"The {typeof(T).Name} document is empty.");
            ContractVersionValidator.ValidateIngress(document);
            return document;
        }
        catch (JsonException exception)
        {
            throw new ContractValidationException($"Invalid {typeof(T).Name} document: {exception.Message}");
        }
    }

    public static T Load<T>(string path) where T : class
    {
        try
        {
            return Parse<T>(File.ReadAllText(path));
        }
        catch (JsonException exception)
        {
            throw new ContractValidationException($"Invalid {typeof(T).Name} document: {exception.Message}");
        }
    }
}