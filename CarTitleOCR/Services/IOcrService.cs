using CarTitleOCR.Models;

namespace CarTitleOCR.Services;

public interface IOcrService
{
    /// <summary>
    /// Analyzes an uploaded document and extracts bicycle title fields.
    /// </summary>
    /// <param name="fileBytes">The raw bytes of the uploaded document.</param>
    /// <param name="contentType">MIME type of the uploaded file.</param>
    /// <returns>A <see cref="CarTitleModel"/> populated with any extracted values.</returns>
    Task<CarTitleModel> ExtractCarTitleFieldsAsync(byte[] fileBytes, string contentType);

    /// <summary>
    /// Analyzes an uploaded continuation sheet and extracts incident fields.
    /// </summary>
    Task<ContinuationSheetModel> ExtractContinuationSheetFieldsAsync(byte[] fileBytes, string contentType);

    /// <summary>
    /// Analyzes an uploaded incident report (CCF-225) and extracts fields.
    /// </summary>
    Task<IncidentReportModel> ExtractIncidentReportFieldsAsync(byte[] fileBytes, string contentType);
}
