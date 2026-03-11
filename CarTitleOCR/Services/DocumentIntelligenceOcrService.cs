using Azure;
using Azure.AI.DocumentIntelligence;
using CarTitleOCR.Models;
using Microsoft.Extensions.Configuration;

namespace CarTitleOCR.Services;

public class DocumentIntelligenceOcrService : IOcrService
{
    private readonly DocumentIntelligenceClient? _client;

    public DocumentIntelligenceOcrService(IConfiguration configuration)
    {
        var endpoint = configuration["AzureDocumentIntelligence:Endpoint"];
        var key = configuration["AzureDocumentIntelligence:Key"];

        if (!string.IsNullOrWhiteSpace(endpoint) && !string.IsNullOrWhiteSpace(key))
            _client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureKeyCredential(key));
    }

    public async Task<CarTitleModel> ExtractCarTitleFieldsAsync(byte[] fileBytes, string contentType)
    {
        if (_client is null)
            throw new InvalidOperationException(
                "Azure Document Intelligence is not configured. " +
                "Please set AzureDocumentIntelligence:Endpoint and AzureDocumentIntelligence:Key in appsettings.json.");
        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-document",
            BinaryData.FromBytes(fileBytes));

        var result = operation.Value;
        var model = new CarTitleModel();

        // Map key-value pairs from the document to the CarTitleModel fields
        if (result.KeyValuePairs != null)
        {
            foreach (var kvp in result.KeyValuePairs)
            {
                var key = kvp.Key?.Content?.Trim() ?? string.Empty;
                var value = kvp.Value?.Content?.Trim();

                MapFieldByKey(model, key, value);
            }
        }

        // Supplement with paragraph text if VIN is still missing (scan for 17-char alphanumeric)
        if (string.IsNullOrWhiteSpace(model.Vin) && result.Paragraphs != null)
        {
            foreach (var paragraph in result.Paragraphs)
            {
                var text = paragraph.Content?.Trim() ?? string.Empty;
                if (IsLikelyVin(text))
                {
                    model.Vin = text;
                    break;
                }
            }
        }

        return model;
    }

    private static void MapFieldByKey(CarTitleModel model, string key, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        var normalizedKey = key.ToUpperInvariant();

        if (Contains(normalizedKey, "VIN", "VEHICLE IDENTIFICATION NUMBER", "VEHICLE ID"))
            model.Vin ??= value;
        else if (Contains(normalizedKey, "YEAR", "MODEL YEAR", "YR"))
            model.Year ??= value;
        else if (Contains(normalizedKey, "MAKE", "VEHICLE MAKE", "MFR", "MANUFACTURER"))
            model.Make ??= value;
        else if (Contains(normalizedKey, "MODEL", "VEHICLE MODEL") && !Contains(normalizedKey, "YEAR"))
            model.VehicleModel ??= value;
        else if (Contains(normalizedKey, "BODY STYLE", "BODY TYPE", "STYLE"))
            model.BodyStyle ??= value;
        else if (Contains(normalizedKey, "COLOR", "COLOUR", "EXTERIOR COLOR"))
            model.Color ??= value;
        else if (Contains(normalizedKey, "ODOMETER", "MILEAGE", "MILES", "READING"))
            model.Odometer ??= value;
        else if (Contains(normalizedKey, "TITLE NUMBER", "TITLE NO", "DOCUMENT NUMBER", "TITLE #"))
            model.TitleNumber ??= value;
        else if (Contains(normalizedKey, "STATE", "ISSUING STATE") && !Contains(normalizedKey, "OWNER"))
            model.StateOfIssuance ??= value;
        else if (Contains(normalizedKey, "PREVIOUS OWNER", "SELLER", "TRANSFEROR", "FROM"))
            model.PreviousOwnerName ??= value;
        else if (Contains(normalizedKey, "NEW OWNER", "BUYER", "PURCHASER", "TRANSFEREE", "TO"))
            model.NewOwnerName ??= value;
        else if (Contains(normalizedKey, "PURCHASE PRICE", "SALE PRICE", "SELLING PRICE", "AMOUNT"))
            model.PurchasePrice ??= value;
        else if (Contains(normalizedKey, "PURCHASE DATE", "SALE DATE", "DATE OF SALE", "DATE SOLD"))
            model.PurchaseDate ??= value;
        else if (Contains(normalizedKey, "LIENHOLDER", "LIEN HOLDER", "LIEN", "FIRST LIEN"))
            model.LienholderName ??= value;
    }

    private static bool Contains(string source, params string[] candidates)
        => candidates.Any(c => source.Contains(c, StringComparison.OrdinalIgnoreCase));

    private static bool IsLikelyVin(string text)
        => text.Length == 17
            && text.All(c => char.IsLetterOrDigit(c))
            && !text.All(char.IsDigit);
}
