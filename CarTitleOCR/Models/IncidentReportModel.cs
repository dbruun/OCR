namespace CarTitleOCR.Models;

public class IncidentReportModel
{
    // Header
    public string? TjjdNumber { get; set; }
    public string? YouthName { get; set; }
    public string? Dorm { get; set; }
    public string? Location { get; set; }

    // Incident Details
    public string? DateOfIncident { get; set; }
    public string? TimeOfIncident { get; set; }
    public string? SecurityCameraNumber { get; set; }
    public string? InstitutionLocation { get; set; }
    public string? ApplicableCodes { get; set; }

    // Description
    public string? Description { get; set; }

    // Follow-up
    public string? ForceUsed { get; set; }
    public string? YouthInjured { get; set; }
    public string? InfirmaryNotified { get; set; }
    public string? InfirmaryNotifiedTime { get; set; }
    public string? InfirmaryNotifiedDate { get; set; }
    public string? PersonNotified { get; set; }
    public string? ContrabandFound { get; set; }

    // Witnesses
    public string? StaffWitnesses { get; set; }
    public string? StaffWitnessJobTitle { get; set; }
    public string? YouthWitnesses { get; set; }

    // Staff Preparing Report
    public string? StaffPreparingReport { get; set; }
}
