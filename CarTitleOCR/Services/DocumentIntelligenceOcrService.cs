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
        model.PurchaseDate = MatchDateValue(transactionBlock, "Purchase Date")
            ?? MatchDateValue(fullText, "Purchase Date");
        model.PurchasePrice = NormalizePrice(
            MatchValue(transactionBlock, @"\bPurchase\s+Price\s*:\s*(\$?\s*[0-9,.]+(?:\.[0-9]{2})?)")
            ?? MatchValue(fullText, @"\bPurchase\s+Price\s*:\s*(\$?\s*[0-9,.]+(?:\.[0-9]{2})?)"));

        var lienBlock = ExtractSection(fullText, "LIENHOLDER", "NOTICE:");
        model.LienholderName = MatchValue(lienBlock, @"\bLien\s+Holder\s+Name\s*:\s*([^\r\n:]+?)(?=\s+Lien\s+Holder\s+Address\s*:|\r|\n|$)")
            ?? MatchValue(fullText, @"\bLien\s+Holder\s+Name\s*:\s*([^\r\n:]+?)(?=\s+Lien\s+Holder\s+Address\s*:|\r|\n|$)");
        model.LienholderAddress = MatchValue(lienBlock, @"\bLien\s+Holder\s+Address\s*:\s*([^\r\n]+)")
            ?? MatchValue(fullText, @"\bLien\s+Holder\s+Address\s*:\s*([^\r\n]+)");

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

    private static string? MatchDateValue(string text, string label)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var normalizedText = NormalizeWhitespace(text);
        var escapedLabel = Regex.Escape(label).Replace("\\ ", "\\s+");
        var pattern = $@"\b{escapedLabel}\s*:\s*((?:January|February|March|April|May|June|July|August|September|October|November|December)\s+\d{{1,2}},\s+\d{{4}})";
        var match = Regex.Match(normalizedText, pattern, RegexOptions.IgnoreCase);
        if (!match.Success || match.Groups.Count < 2)
            return null;

        return NormalizeWhitespace(match.Groups[1].Value);
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

    public async Task<ContinuationSheetModel> ExtractContinuationSheetFieldsAsync(byte[] fileBytes, string contentType)
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

        return ParseContinuationSheet(fullText, lines);
    }

    private static ContinuationSheetModel ParseContinuationSheet(string fullText, List<string> lines)
    {
        var model = new ContinuationSheetModel();

        // Date — typically "Date:04/14/26" or "Date: 04/14/26"
        model.Date = MatchValue(fullText, @"\bDate\s*[:\.]?\s*(\d{1,2}/\d{1,2}/\d{2,4})");

        // Time — e.g. "Time: 1:15 pm" or "1:15 pm"
        model.Time = MatchValue(fullText, @"\bTime\s*[:\.]?\s*(\d{1,2}[:\.]?\d{2}\s*(?:am|pm|AM|PM)?)");

        // Incident Pointer #
        model.IncidentPointer = MatchValue(fullText, @"Incident\s+Pointer\s*#?\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|\r|\n|$)");

        // TJJD Number
        model.TjjdNumber = MatchValue(fullText, @"TJJD\s+Number\s*[:\.]?\s*(\d+)");

        // Youth's Name — "Youth's Name: Last First MI" or similar
        model.YouthName = MatchValue(fullText, @"Youth'?s?\s+Name\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|\r|\n|$)");

        // Location
        model.Location = MatchValue(fullText, @"\bLocation\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|Form\s+Number|\r|\n|$)");

        // Form Number/Name/Item
        model.FormNumberNameItem = MatchValue(fullText, @"Form\s+Number/?Name/?Item\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|\r|\n|$)");

        // Description — everything in the main body area after the header fields.
        // Look for the content after the header block (after "Form Number" line or "Report" line)
        // and before the signature/print name area.
        model.Description = ExtractDescription(fullText, lines);

        // Strip any extracted header values that leaked into the description start
        if (!string.IsNullOrWhiteSpace(model.Description))
        {
            var headerValues = new[] { model.Location, model.FormNumberNameItem, model.Date,
                                       model.Time, model.TjjdNumber, model.YouthName, model.IncidentPointer };
            foreach (var val in headerValues)
            {
                if (!string.IsNullOrWhiteSpace(val) && model.Description.StartsWith(val, StringComparison.OrdinalIgnoreCase))
                {
                    model.Description = model.Description[val.Length..].TrimStart();
                }
            }
        }

        return model;
    }

    private static string? ExtractDescription(string fullText, List<string> lines)
    {
        // Anchor on the "Form Number" line — the last header field on CCF-500.
        // Capture everything AFTER that line until we hit a footer marker.
        // Only search the first portion of lines for the anchor to avoid
        // accidentally matching body text.
        int anchorIndex = -1;
        int searchLimit = Math.Min(lines.Count, 20); // headers are always in the first ~15 lines

        for (int i = 0; i < searchLimit; i++)
        {
            var trimmed = lines[i].Trim();
            if (Regex.IsMatch(trimmed, @"Form\s+Number", RegexOptions.IgnoreCase))
            {
                anchorIndex = i;
                break;
            }
        }

        // Fallback: try Location line if Form Number wasn't found
        if (anchorIndex < 0)
        {
            for (int i = 0; i < searchLimit; i++)
            {
                if (Regex.IsMatch(lines[i].Trim(), @"Location\s*[:\.]", RegexOptions.IgnoreCase))
                {
                    anchorIndex = i;
                    break;
                }
            }
        }

        // Last resort: skip first 5 lines (rough header skip)
        if (anchorIndex < 0)
            anchorIndex = Math.Min(5, lines.Count - 1);

        // Collect body lines from after the anchor until a footer marker,
        // skipping any remaining header-field lines that OCR placed after the anchor.
        var headerFieldPatterns = new[]
        {
            @"Location\s*[:\.]",
            @"Form\s+Number",
            @"TJJD\s+Number",
            @"Youth'?s?\s+Name",
            @"Incident\s+Pointer",
            @"Date\s*[:\.]",
            @"Time\s*[:\.]",
            @"^\s*(Last|First|MI)\s*$",
        };

        var bodyLines = new List<string>();
        for (int i = anchorIndex + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();

            if (Regex.IsMatch(trimmed, @"(Print\s+Name|Signature|End\s+of\s+State|CCF-\d+|Page\s+\d+\s+of\s+\d+)", RegexOptions.IgnoreCase))
                break;

            // Skip any straggling header fields that OCR placed after the anchor
            bool isHeader = false;
            foreach (var pattern in headerFieldPatterns)
            {
                if (Regex.IsMatch(trimmed, pattern, RegexOptions.IgnoreCase))
                {
                    isHeader = true;
                    break;
                }
            }
            if (isHeader)
                continue;

            if (!string.IsNullOrWhiteSpace(trimmed))
                bodyLines.Add(trimmed);
        }

        var description = string.Join(" ", bodyLines).Trim();
        return string.IsNullOrWhiteSpace(description) ? null : description;
    }

    public async Task<IncidentReportModel> ExtractIncidentReportFieldsAsync(byte[] fileBytes, string contentType)
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

        return ParseIncidentReport(fullText, lines);
    }

    private static IncidentReportModel ParseIncidentReport(string fullText, List<string> lines)
    {
        var model = new IncidentReportModel();

        // TJJD Number
        model.TjjdNumber = MatchValue(fullText, @"TJJD\s+Number\s*[:\.]?\s*(\d[\d\s]*\d)");

        // Youth Name — "Youth Name: Last First MI"
        model.YouthName = MatchValue(fullText, @"Youth\s+Name\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|Deri|Dorm|\r|\n|$)");

        // Dorm
        model.Dorm = MatchValue(fullText, @"\bDorm\s*[:\.]?\s*(\w+)");

        // Location
        model.Location = MatchValue(fullText, @"\bLocation\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|\r|\n|$)");

        // Date of Incident
        model.DateOfIncident = MatchValue(fullText, @"Date\s+of\s+Incident\s*[:\.]?\s*(\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4})");

        // Time of Incident
        model.TimeOfIncident = MatchValue(fullText, @"Time\s+of\s+Incident\s*[:\.]?\s*(\d{1,2}[:\.]?\s*\d{2}\s*(?:am|pm|AM|PM)?)");

        // Security Camera Number
        model.SecurityCameraNumber = MatchValue(fullText, @"Security\s+Camera\s+(?:number|num|#)\s*[:\.]?\s*(\w+)");

        // Institution location — look for checked boxes (DO Dorm, RA Recreation Area, etc.)
        model.InstitutionLocation = ExtractCheckedItems(fullText, new[]
        {
            ("RA", "Recreation Area"), ("CH", "Chapel"), ("IF", "Infirmary"),
            ("CL", "Class / School"), ("DE", "Security"), ("FR", "Furlough"),
            ("OC", "Off Campus"), ("OP", "Other on Campus"), ("CF", "Cafeteria"),
            ("DO", "Dorm"), ("FG", "Facility Grounds"),
        });

        // Applicable codes
        model.ApplicableCodes = ExtractCheckedItems(fullText, new[]
        {
            ("RF", "Rule Violation with Security Referral"),
            ("RM", "Resisting Movement/Resisting Removal"),
            ("IR", "Rule Violation without Sec. Referral"),
            ("MV", "Medium Restriction Facility - Rule Violation"),
            ("PV", "Parole - Rule Violation"),
            ("AB", "Alleged Abuse or Neglect"),
            ("AN", "Arrest for Other Than Felony Offense"),
            ("FO", "Arrest for Felony Offense"),
            ("AH", "Awaiting Hearing/Transportation"),
            ("ET", "24-Hr Sec. Extension - Hearing Interference"),
            ("CI", "Injury Requiring Hospital Admit"),
            ("DH", "Death"), ("HO", "Hostage Incident"),
            ("PT", "Protective Custody"),
            ("SW", "Self-Referral to Security - Safety"),
            ("SY", "Self-Referral - Mental Health"),
            ("SZ", "Self-Referral - Other"),
            ("SI", "Staff Injury"), ("SV", "Suicidal Behavior"),
            ("SN", "Suicidal Ideation"),
            ("VS", "Violation of Security Program"),
            ("MO", "Medical Segregation"),
            ("SE", "Strip Search (non-routine)"),
            ("YI", "Youth Injury without Hospitalization"),
            ("SS", "Non-Disciplinary Transfer Criteria"),
            ("MH", "Mental Health Status Review"),
            ("IP", "Isolation Phoenix"),
        });

        // Description (Section II) — free text after "DESCRIPTION" header
        model.Description = ExtractIncidentDescription(lines);

        // Force used
        model.ForceUsed = ExtractYesNo(fullText, @"Was\s+force\s+used");

        // Youth injured
        model.YouthInjured = ExtractYesNo(fullText, @"Was\s+youth\s+injured");

        // Infirmary notified
        model.InfirmaryNotified = ExtractYesNo(fullText, @"Was\s+infirmary\s+staff\s+notified");
        model.InfirmaryNotifiedTime = MatchValue(fullText, @"infirmary\s+staff\s+notified.*?time\s*[:\.]?\s*(\d{1,2}[:\.]?\s*\d{2}\s*(?:am|pm)?)");
        model.InfirmaryNotifiedDate = MatchValue(fullText, @"infirmary\s+staff\s+notified.*?Date\s*[:\.]?\s*(\d{1,2}[/\-]\d{1,2}[/\-]\d{2,4})");
        model.PersonNotified = MatchValue(fullText, @"Person\s+notified\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|\r|\n|$)");

        // Contraband
        model.ContrabandFound = ExtractYesNo(fullText, @"Was\s+contraband\s+found");

        // Staff witnesses
        model.StaffWitnesses = MatchValue(fullText, @"Staff\s+Witnesses\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|Job\s+Title|\r|\n|$)");
        model.StaffWitnessJobTitle = MatchValue(fullText, @"Job\s+Title\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|Youth\s+Witnesses|\r|\n|$)");

        // Youth witnesses
        model.YouthWitnesses = MatchValue(fullText, @"Youth\s+Witnesses\s*[:\.]?\s*([^\r\n]+?)(?=\s{2,}|TJJD\s+Number|\r|\n|$)");

        // Staff preparing report
        model.StaffPreparingReport = MatchValue(fullText, @"STAFF\s+PREPARING\s+REPORT\s*[:\.]?\s*(?:Printed\s+Name\s*[:\.]?\s*)?([^\r\n]+?)(?=\s{2,}|\r|\n|$)");

        return model;
    }

    private static string? ExtractCheckedItems(string fullText, (string code, string label)[] items)
    {
        var found = new List<string>();
        foreach (var (code, label) in items)
        {
            // Look for checked box patterns: ☑ CODE, [X] CODE, ✓ CODE, or just the code preceded by a check indicator
            var pattern = $@"(?:☑|☒|\[X\]|✓|✔|⊠)\s*{Regex.Escape(code)}\b";
            if (Regex.IsMatch(fullText, pattern, RegexOptions.IgnoreCase))
            {
                found.Add($"{code} - {label}");
            }
        }
        return found.Count > 0 ? string.Join("; ", found) : null;
    }

    private static string? ExtractYesNo(string fullText, string labelPattern)
    {
        // The CCF-225 form layout has "☐Yes ☐No" BEFORE the label text, e.g.:
        //   "☐Yes ☐No  Was force used during this incident?"
        // OCR may render ☐ as □, D, or nothing. Checked boxes may appear as ☑, ☒, ✓, [X], ■, etc.
        // Strategy: find the label, grab a window BEFORE and AFTER it, look for Yes/No.
        var labelMatch = Regex.Match(fullText, labelPattern, RegexOptions.IgnoreCase);
        if (!labelMatch.Success)
            return null;

        // Window: 200 chars before the label + the label + 200 chars after
        var windowStart = Math.Max(0, labelMatch.Index - 200);
        var windowEnd = Math.Min(fullText.Length, labelMatch.Index + labelMatch.Length + 200);
        var window = fullText[windowStart..windowEnd];

        // Look for the line containing the label within the window
        // Split into the part before the label and after
        var labelInWindow = labelMatch.Index - windowStart;
        var beforeLabel = window[..labelInWindow];
        var afterLabel = window[labelInWindow..];

        // Check for Yes/No in the text before the label (where they typically appear on CCF-225)
        // Look for the closest Yes/No to the label
        var yesBeforeMatches = Regex.Matches(beforeLabel, @"\bYes\b", RegexOptions.IgnoreCase);
        var noBeforeMatches = Regex.Matches(beforeLabel, @"\bNo\b", RegexOptions.IgnoreCase);

        // Get the closest Yes and No before the label
        var lastYesBefore = yesBeforeMatches.Count > 0 ? yesBeforeMatches[^1] : null;
        var lastNoBefore = noBeforeMatches.Count > 0 ? noBeforeMatches[^1] : null;

        // Also check after the label
        var yesAfter = Regex.Match(afterLabel, @"\bYes\b", RegexOptions.IgnoreCase);
        var noAfter = Regex.Match(afterLabel, @"\bNo\b", RegexOptions.IgnoreCase);

        // Find the nearest Yes and No (before or after)
        int yesDistance = int.MaxValue;
        int noDistance = int.MaxValue;

        if (lastYesBefore != null)
            yesDistance = Math.Min(yesDistance, labelInWindow - lastYesBefore.Index);
        if (yesAfter.Success)
            yesDistance = Math.Min(yesDistance, yesAfter.Index);

        if (lastNoBefore != null)
            noDistance = Math.Min(noDistance, labelInWindow - lastNoBefore.Index);
        if (noAfter.Success)
            noDistance = Math.Min(noDistance, noAfter.Index);

        // If both are found on the same line before the label, we need to determine which is checked.
        // On this form, the checked one typically has a filled marker before it.
        // Since we can't reliably detect check state from OCR, look at the text immediately 
        // before "Yes" and "No" for filled vs empty box indicators.
        if (lastYesBefore != null && lastNoBefore != null)
        {
            // Get the character(s) right before each word
            var yesPrefix = GetPrecedingChars(beforeLabel, lastYesBefore.Index, 3);
            var noPrefix = GetPrecedingChars(beforeLabel, lastNoBefore.Index, 3);

            var filledPattern = @"[☑☒✓✔⊠■⬛\u2611\u2612]";
            var emptyPattern = @"[☐□◻\u2610]";

            bool yesHasFilled = Regex.IsMatch(yesPrefix, filledPattern);
            bool noHasFilled = Regex.IsMatch(noPrefix, filledPattern);
            bool yesHasEmpty = Regex.IsMatch(yesPrefix, emptyPattern);
            bool noHasEmpty = Regex.IsMatch(noPrefix, emptyPattern);

            if (yesHasFilled && !noHasFilled) return "Yes";
            if (noHasFilled && !yesHasFilled) return "No";
            if (yesHasEmpty && !noHasEmpty) return "No"; // only Yes has empty box → No is checked
            if (noHasEmpty && !yesHasEmpty) return "Yes"; // only No has empty box → Yes is checked
        }

        // Fallback: return whichever Yes/No is closest to the label
        if (yesDistance < noDistance) return "Yes";
        if (noDistance < yesDistance) return "No";

        // Last resort: find first Yes/No in the whole window
        var fallback = Regex.Match(window, @"\b(Yes|No)\b", RegexOptions.IgnoreCase);
        return fallback.Success ? fallback.Groups[1].Value : null;
    }

    private static string GetPrecedingChars(string text, int index, int count)
    {
        var start = Math.Max(0, index - count);
        return text[start..index];
    }

    private static string? ExtractIncidentDescription(List<string> lines)
    {
        // Find the "DESCRIPTION" header line, capture everything after it
        // until we hit the follow-up questions (Was force used, Staff Witnesses, etc.)
        int descStart = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (Regex.IsMatch(lines[i].Trim(), @"DESCRIPTION", RegexOptions.IgnoreCase))
            {
                descStart = i;
                break;
            }
        }

        if (descStart < 0)
            return null;

        // Collect ALL lines between DESCRIPTION and the follow-up section
        var bodyLines = new List<string>();
        for (int i = descStart + 1; i < lines.Count; i++)
        {
            var trimmed = lines[i].Trim();

            // Stop at follow-up section markers
            if (Regex.IsMatch(trimmed, @"(Was\s+force\s+used|Was\s+youth\s+injured|Was\s+infirmary|Was\s+contraband|Staff\s+Witnesses|Page\s+\d+\s+of|STAFF\s+PREPARING)", RegexOptions.IgnoreCase))
                break;

            if (!string.IsNullOrWhiteSpace(trimmed))
                bodyLines.Add(trimmed);
        }

        var description = string.Join(" ", bodyLines).Trim();

        // Strip known printed template sentences from the combined text
        var templatePhrases = new[]
        {
            @"Summarize\s+the\s+incident\s+in\s+the\s+space\s+provided\.?",
            @"Describe\s+in\s+detail\s+actions\s+and\s+statements\s+of\s+youth\s+and\s+staff\s+during\s+incident,?\s*including\s+prior\s+failed\s+interventions\.?",
            @"Describe\s+any\s+contraband\s+found\.?",
            @"Attach\s+a\s+CCF-?500\s+Continuation\s+Form\s+providing\s+additional\s+details\s+as\s+necessary\.?",
            @"Any\s+additional\s+staff\s+who\s+witnessed\s+or\s+participated\s+in\s+the\s+incident\s+should\s+attach\s+a\s+CCF-?500\s+form\s+describing\s+the\s+incident\.?",
            @"If\s+yes,?\s+attach\s+CCF-?260\s+Use\s+of\s+Force\s+Report\.?",
            @"If\s+yes,?\s+describe\s+location\s+and\s+type\s+of\s+injury\s+in\s+section\s+above\.?",
            @"If\s+yes,?\s+contraband\s+delivered\s+to:?",
            @"CCF-?500\s+form\(s\)\s+attached",
            @"•\s*",
            @"·\s*",
        };

        foreach (var phrase in templatePhrases)
        {
            description = Regex.Replace(description, phrase, " ", RegexOptions.IgnoreCase).Trim();
        }

        // Clean up extra whitespace
        description = Regex.Replace(description, @"\s{2,}", " ").Trim();

        return string.IsNullOrWhiteSpace(description) ? null : description;
    }
}
