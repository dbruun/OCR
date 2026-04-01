using CarTitleOCR.Models;
using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;

namespace CarTitleOCR.Services;

public class FraudDetectionService : IFraudDetectionService
{
    private static readonly ConcurrentDictionary<string, byte> SeenVins = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> WatchlistedVins = new(StringComparer.OrdinalIgnoreCase)
    {
        "7RDX5KLM2PS483621"
    };

    private static readonly HashSet<string> ValidStateCodes = new(StringComparer.OrdinalIgnoreCase)
    {
        "AL", "AK", "AZ", "AR", "CA", "CO", "CT", "DE", "FL", "GA",
        "HI", "ID", "IL", "IN", "IA", "KS", "KY", "LA", "ME", "MD",
        "MA", "MI", "MN", "MS", "MO", "MT", "NE", "NV", "NH", "NJ",
        "NM", "NY", "NC", "ND", "OH", "OK", "OR", "PA", "RI", "SC",
        "SD", "TN", "TX", "UT", "VT", "VA", "WA", "WV", "WI", "WY",
        "DC"
    };

    public FraudCheckResult Evaluate(CarTitleModel title, bool registerVin)
    {
        var result = new FraudCheckResult();

        CheckVin(title, result, registerVin);
        CheckYear(title, result);
        CheckState(title.StateOfIssuance, "Issuing state is invalid.", result, nameof(CarTitleModel.StateOfIssuance));
        CheckState(title.PreviousOwnerState, "Previous owner state is invalid.", result, nameof(CarTitleModel.PreviousOwnerState));
        CheckState(title.NewOwnerState, "New owner state is invalid.", result, nameof(CarTitleModel.NewOwnerState));
        CheckZip(title.PreviousOwnerZip, "Previous owner ZIP code is invalid.", result, nameof(CarTitleModel.PreviousOwnerZip));
        CheckZip(title.NewOwnerZip, "New owner ZIP code is invalid.", result, nameof(CarTitleModel.NewOwnerZip));
        CheckPrice(title, result);
        CheckPurchaseDate(title, result);

        result.RequiresManualReview = result.Score >= 40;
        return result;
    }

    private static void CheckVin(CarTitleModel title, FraudCheckResult result, bool registerVin)
    {
        if (string.IsNullOrWhiteSpace(title.Vin))
        {
            AddFlag(result, 20, "VIN is missing.", nameof(CarTitleModel.Vin));
            return;
        }

        var vin = title.Vin.Trim().ToUpperInvariant();
        if (!Regex.IsMatch(vin, "^[A-HJ-NPR-Z0-9]{6,17}$"))
        {
            AddFlag(result, 40, "VIN format is invalid.", nameof(CarTitleModel.Vin));
            return;
        }

        if (WatchlistedVins.Contains(vin))
            AddFlag(result, 60, "VIN matched the fraud watchlist.", nameof(CarTitleModel.Vin));

        if (SeenVins.ContainsKey(vin))
            AddFlag(result, 50, "Duplicate VIN detected from a previous upload.", nameof(CarTitleModel.Vin));

        if (registerVin)
            SeenVins.TryAdd(vin, 0);
    }

    private static void CheckYear(CarTitleModel title, FraudCheckResult result)
    {
        if (string.IsNullOrWhiteSpace(title.Year))
            return;

        if (!int.TryParse(title.Year, out var year))
        {
            AddFlag(result, 25, "Model year is not numeric.", nameof(CarTitleModel.Year));
            return;
        }

        var nextYear = DateTime.UtcNow.Year + 1;
        if (year < 1970 || year > nextYear)
            AddFlag(result, 25, "Model year falls outside the expected range.", nameof(CarTitleModel.Year));
    }

    private static void CheckState(string? value, string message, FraudCheckResult result, params string[] reviewFields)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!ValidStateCodes.Contains(value.Trim()))
            AddFlag(result, 15, message, reviewFields);
    }

    private static void CheckZip(string? value, string message, FraudCheckResult result, params string[] reviewFields)
    {
        if (string.IsNullOrWhiteSpace(value))
            return;

        if (!Regex.IsMatch(value.Trim(), "^\\d{5}(-\\d{4})?$") )
            AddFlag(result, 10, message, reviewFields);
    }

    private static void CheckPrice(CarTitleModel title, FraudCheckResult result)
    {
        if (string.IsNullOrWhiteSpace(title.PurchasePrice))
        {
            AddFlag(result, 10, "Purchase price is missing.", nameof(CarTitleModel.PurchasePrice));
            return;
        }

        var normalized = title.PurchasePrice.Replace("$", string.Empty).Replace(",", string.Empty).Trim();
        if (!decimal.TryParse(normalized, NumberStyles.Number | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var price))
        {
            AddFlag(result, 15, "Purchase price is not a valid amount.", nameof(CarTitleModel.PurchasePrice));
            return;
        }

        if (price < 50m || price > 20000m)
            AddFlag(result, 20, "Purchase price falls outside the expected range.", nameof(CarTitleModel.PurchasePrice));
    }

    private static void CheckPurchaseDate(CarTitleModel title, FraudCheckResult result)
    {
        if (string.IsNullOrWhiteSpace(title.PurchaseDate))
        {
            AddFlag(result, 10, "Purchase date is missing.", nameof(CarTitleModel.PurchaseDate));
            return;
        }

        if (!DateTime.TryParse(title.PurchaseDate, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var purchaseDate))
        {
            AddFlag(result, 15, "Purchase date is not a valid date.", nameof(CarTitleModel.PurchaseDate));
            return;
        }

        if (purchaseDate.Date > DateTime.Today)
            AddFlag(result, 35, "Purchase date is in the future.", nameof(CarTitleModel.PurchaseDate));
    }

    private static void AddFlag(FraudCheckResult result, int score, string message, params string[] reviewFields)
    {
        result.Score += score;
        result.Flags.Add(message);

        foreach (var reviewField in reviewFields.Where(field => !string.IsNullOrWhiteSpace(field)))
            result.ReviewFields.Add(reviewField);
    }
}