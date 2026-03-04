using CarTitleOCR.Models;

namespace CarTitleOCR.Services;

public interface IOcrService
{
    /// <summary>
    /// Analyzes an uploaded document and extracts car title fields.
    /// </summary>
    /// <param name="fileBytes">The raw bytes of the uploaded document.</param>
    /// <param name="contentType">MIME type of the uploaded file.</param>
    /// <returns>A <see cref="CarTitleModel"/> populated with any extracted values.</returns>
    Task<CarTitleModel> ExtractCarTitleFieldsAsync(byte[] fileBytes, string contentType);
}
