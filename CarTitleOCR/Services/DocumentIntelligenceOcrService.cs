using Azure;
using Azure.AI.DocumentIntelligence;
using Azure.Identity;
using CarTitleOCR.Models;
using Microsoft.Extensions.Configuration;
using System.Text.RegularExpressions;

namespace CarTitleOCR.Services;

public class DocumentIntelligenceOcrService : IOcrService
{
    private readonly DocumentIntelligenceClient? _client;

    public DocumentIntelligenceOcrService(IConfiguration configuration)
    {
        var endpoint = configuration["AzureDocumentIntelligence:Endpoint"];

        if (!string.IsNullOrWhiteSpace(endpoint))
            _client = new DocumentIntelligenceClient(new Uri(endpoint), new AzureCliCredential());
    }

    public async Task<CarTitleModel> ExtractCarTitleFieldsAsync(byte[] fileBytes, string contentType)
    {
        if (_client is null)
            throw new InvalidOperationException(
                "Azure Document Intelligence is not configured. " +
                "Please set AzureDocumentIntelligence:Endpoint in appsettings.json and ensure you are authenticated with Azure (az login or Visual Studio).");

        var operation = await _client.AnalyzeDocumentAsync(
            WaitUntil.Completed,
            "prebuilt-layout",
            BinaryData.FromBytes(fileBytes));

        var result = operation.Value;
        var lines = ExtractLines(result);
        var fullText = string.Join("\n", lines);

        return ParseModel(fullText);
    }

    private static List<string> ExtractLines(AnalyzeResult result)
    {
        var lines = new List<string>();

        if (result.Pages is null)
            return lines;

        foreach (var page in result.Pages)
        {
            if (page.Lines is null)
                continue;

            foreach (var line in page.Lines)
            {
                var content = NormalizeWhitespace(line.Content);
                if (!string.IsNullOrWhiteSpace(content))
                    lines.Add(content);
            }
        }

        return lines;
    }

    private static CarTitleModel ParseModel(string fullText)
    {
        var model = new CarTitleModel();

        model.Vin = MatchValue(fullText, @"\bVIN\s*:\s*([A-Z0-9]{6,17})");
        model.Year = MatchValue(fullText, @"\bYear\s*:\s*(\d{4})");
        model.Make = MatchValue(fullText, @"\bMake\s*:\s*([^\r\n:]+?)(?=\s+Model\s*:|\s+Body\s+Style\s*:|\r|\n|$)");
        model.BicycleModel = MatchValue(fullText, @"\bModel\s*:\s*([^\r\n:]+?)(?=\s+Body\s+Style\s*:|\r|\n|$)");
        model.BodyStyle = MatchValue(fullText, @"\bBody\s+Style\s*:\s*([^\r\n:]+?)(?=\s+Color\s*:|\s+Odometer\s*:|\r|\n|$)");
        model.Color = MatchValue(fullText, @"\bColor\s*:\s*([^\r\n:]+?)(?=\s+Odometer\s*:|\r|\n|$)");
        model.Odometer = MatchValue(fullText, @"\bOdometer\s*:\s*([^\r\n:]+)");
        model.TitleNumber = MatchValue(fullText, @"\bTitle\s+Number\s*:\s*([^\r\n]+)");
        model.StateOfIssuance = MatchValue(fullText, @"\bState\s+of\s+Issuance\s*:\s*([A-Z]{2})");

        var previousOwnerBlock = ExtractSection(
            fullText,
            "PREVIOUS OWNER",
            "NEW OWNER",
            "TRANSACTION DETAILS",
            "LIENHOLDER",
            "NOTICE:");
        ApplyOwnerSection(previousOwnerBlock, true, model);

        var newOwnerBlock = ExtractSection(
            fullText,
            "NEW OWNER",
            "TRANSACTION DETAILS",
            "LIENHOLDER",
            "NOTICE:");
        ApplyOwnerSection(newOwnerBlock, false, model);

        var transactionBlock = ExtractSection(
            fullText,
            "TRANSACTION DETAILS",
            "LIENHOLDER",
            "NOTICE:");
        model.PurchaseDate = MatchValue(transactionBlock, @"\bPurchase\s+Date\s*:\s*([^\r\n:]+?)(?=\s+Purchase\s+Price\s*:|\r|\n|$)");
        model.PurchasePrice = NormalizePrice(MatchValue(transactionBlock, @"\bPurchase\s+Price\s*:\s*(\$?\s*[0-9,.]+(?:\.[0-9]{2})?)"));

        var lienBlock = ExtractSection(fullText, "LIENHOLDER", "NOTICE:");
        model.LienholderName = MatchValue(lienBlock, @"\bLien\s+Holder\s+Name\s*:\s*([^\r\n:]+?)(?=\s+Lien\s+Holder\s+Address\s*:|\r|\n|$)");
        model.LienholderAddress = MatchValue(lienBlock, @"\bLien\s+Holder\s+Address\s*:\s*([^\r\n]+)");

        return model;
    }

    private static void ApplyOwnerSection(string sectionText, bool isPreviousOwner, CarTitleModel model)
    {
        if (string.IsNullOrWhiteSpace(sectionText))
            return;

        var name = MatchValue(sectionText, @"\bName\s*:\s*([^\r\n:]+?)(?=\s+Address\s*:|\r|\n|$)");
        var address = MatchValue(sectionText, @"\bAddress\s*:\s*([^\r\n:]+?)(?=\s+City\s*:|\r|\n|$)");
        var city = MatchValue(sectionText, @"\bCity\s*:\s*([^\r\n:]+?)(?=\s+State\s*:|\r|\n|$)");
        var state = MatchValue(sectionText, @"\bState\s*:\s*([A-Z]{2})(?=\s+ZIP(?:\s+Code)?\s*:|\r|\n|$)");
        var zip = MatchValue(sectionText, @"\bZIP(?:\s+Code)?\s*:\s*(\d{5}(?:-\d{4})?)");

        if (isPreviousOwner)
        {
            model.PreviousOwnerName = name;
            model.PreviousOwnerAddress = address;
            model.PreviousOwnerCity = city;
            model.PreviousOwnerState = state;
            model.PreviousOwnerZip = zip;
        }
        else
        {
            model.NewOwnerName = name;
            model.NewOwnerAddress = address;
            model.NewOwnerCity = city;
            model.NewOwnerState = state;
            model.NewOwnerZip = zip;
        }
    }

    private static string ExtractSection(string text, string startHeader, params string[] endHeaders)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var startIndex = text.IndexOf(startHeader, StringComparison.OrdinalIgnoreCase);
        if (startIndex < 0)
            return string.Empty;

        var endIndex = text.Length;
        foreach (var endHeader in endHeaders)
        {
            var candidateIndex = text.IndexOf(endHeader, startIndex + startHeader.Length, StringComparison.OrdinalIgnoreCase);
            if (candidateIndex >= 0)
                endIndex = Math.Min(endIndex, candidateIndex);
        }

        return text[startIndex..endIndex];
    }

    private static string? MatchValue(string text, string pattern)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = Regex.Match(text, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
        if (!match.Success || match.Groups.Count < 2)
            return null;

        var value = NormalizeWhitespace(match.Groups[1].Value);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim('|', ' ');
    }

    private static string NormalizeWhitespace(string? text)
        => string.IsNullOrWhiteSpace(text)
            ? string.Empty
            : Regex.Replace(text, @"\s+", " ").Trim();

    private static string? NormalizePrice(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        return value.Replace("$", string.Empty).Trim();
    }
}
