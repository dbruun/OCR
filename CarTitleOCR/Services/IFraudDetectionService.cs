using CarTitleOCR.Models;

namespace CarTitleOCR.Services;

public interface IFraudDetectionService
{
    FraudCheckResult Evaluate(CarTitleModel title, bool registerVin);
}