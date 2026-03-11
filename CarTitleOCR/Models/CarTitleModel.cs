namespace CarTitleOCR.Models;

public class CarTitleModel
{
    // Vehicle Information
    public string? Vin { get; set; }
    public string? Year { get; set; }
    public string? Make { get; set; }
    public string? VehicleModel { get; set; }
    public string? BodyStyle { get; set; }
    public string? Color { get; set; }
    public string? Odometer { get; set; }
    public string? TitleNumber { get; set; }
    public string? StateOfIssuance { get; set; }

    // Previous Owner
    public string? PreviousOwnerName { get; set; }
    public string? PreviousOwnerAddress { get; set; }
    public string? PreviousOwnerCity { get; set; }
    public string? PreviousOwnerState { get; set; }
    public string? PreviousOwnerZip { get; set; }

    // New Owner
    public string? NewOwnerName { get; set; }
    public string? NewOwnerAddress { get; set; }
    public string? NewOwnerCity { get; set; }
    public string? NewOwnerState { get; set; }
    public string? NewOwnerZip { get; set; }

    // Transaction
    public string? PurchasePrice { get; set; }
    public string? PurchaseDate { get; set; }

    // Lienholder
    public string? LienholderName { get; set; }
    public string? LienholderAddress { get; set; }
}
