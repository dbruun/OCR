namespace CarTitleOCR.Models;

public class FraudCheckResult
{
    public int Score { get; set; }
    public bool RequiresManualReview { get; set; }
    public List<string> Flags { get; set; } = new();
    public HashSet<string> ReviewFields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}