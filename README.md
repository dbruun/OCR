# DMV OCR Demo

Internal DMV-oriented Blazor Server demo for processing bicycle title documents with OCR, review assistance, and basic fraud signaling.

## What It Does

This project lets DMV staff:

- Upload a title PDF or image and extract fields with Azure AI Document Intelligence
- Review OCR-populated application fields in a single form
- Surface fraud-review signals during OCR review instead of waiting for submission
- Use an internal-facing AI assistant for DMV processing, exception handling, and review guidance

The application is built as a .NET 8 Blazor Server app.

## Main Features

- OCR extraction for title fields including VIN, year, make, model, owner details, transaction details, and lienholder details
- Internal DMV staff assistant backed by Azure AI Foundry
- Basic fraud rules for:
	- invalid or missing VIN
	- watchlisted VINs
	- duplicate VINs within the running app session
	- invalid year
	- invalid state codes
	- invalid ZIP codes
	- invalid or suspicious purchase price
	- invalid or future purchase date
- Field-level review badges:
	- green `OCR` for normal OCR-populated fields
	- yellow `Review` for OCR-populated fields implicated in fraud checks

## Project Structure

- [CarTitleOCR](c:\Repos\dmvOCR\OCR\CarTitleOCR): Blazor Server application
- [CarTitleOCR/Services/DocumentIntelligenceOcrService.cs](c:\Repos\dmvOCR\OCR\CarTitleOCR\Services\DocumentIntelligenceOcrService.cs): OCR extraction and parsing
- [CarTitleOCR/Services/FraudDetectionService.cs](c:\Repos\dmvOCR\OCR\CarTitleOCR\Services\FraudDetectionService.cs): rules-based fraud detection
- [CarTitleOCR/Services/FoundryAgentService.cs](c:\Repos\dmvOCR\OCR\CarTitleOCR\Services\FoundryAgentService.cs): internal DMV chat assistant integration
- [sample-bicycle-title.html](c:\Repos\dmvOCR\OCR\sample-bicycle-title.html): standard demo title document
- [sample-bicycle-title-fraud.html](c:\Repos\dmvOCR\OCR\sample-bicycle-title-fraud.html): fraud-review demo document

## Requirements

- .NET 8 SDK
- Azure CLI
- Access to:
	- Azure AI Document Intelligence
	- Azure AI Foundry project

## Local Configuration

This repo is set up to avoid checking secrets into git.

Committed configuration files are sanitized. Local values should be stored with .NET user-secrets.

Example configuration is in:

- [CarTitleOCR/appsettings.Development.example.json](c:\Repos\dmvOCR\OCR\CarTitleOCR\appsettings.Development.example.json)

### Set Local Secrets

From [CarTitleOCR](c:\Repos\dmvOCR\OCR\CarTitleOCR):

```powershell
dotnet user-secrets set "AzureDocumentIntelligence:Endpoint" "https://your-doc-intel-resource.cognitiveservices.azure.com/"
dotnet user-secrets set "AzureAIFoundry:Endpoint" "https://your-project.services.ai.azure.com/api/projects/your-project"
dotnet user-secrets set "AzureAIFoundry:TenantId" "your-tenant-id"
dotnet user-secrets set "AzureAIFoundry:ModelDeploymentName" "gpt-4o"
dotnet user-secrets set "AzureAIFoundry:AgentName" "TitleRegistrationAssistant"
```

To inspect local secrets:

```powershell
dotnet user-secrets list
```

## Azure Authentication

Both OCR and Foundry use Azure CLI authentication in the current implementation.

Log in with the correct tenant before running:

```powershell
az login --tenant <your-tenant-id> --use-device-code
```

## Running the App

From [CarTitleOCR](c:\Repos\dmvOCR\OCR\CarTitleOCR):

```powershell
dotnet run
```

Default local URL:

```text
http://localhost:5071
```

Note:

- You may see a local warning about HTTPS redirection not finding a port. That does not block normal HTTP testing for this demo.

## Demo Flow

### Standard OCR Demo

1. Open [sample-bicycle-title.html](c:\Repos\dmvOCR\OCR\sample-bicycle-title.html) in a browser.
2. Print to PDF.
3. Upload the PDF in the app.
4. Review the OCR-populated fields.

### Fraud Review Demo

1. Open [sample-bicycle-title-fraud.html](c:\Repos\dmvOCR\OCR\sample-bicycle-title-fraud.html) in a browser.
2. Print to PDF.
3. Upload the PDF in the app.
4. The app should flag fraud signals during OCR review.

Current fraud demo signals include:

- watchlisted VIN
- future purchase date

Flagged fields show a yellow `Review` badge instead of a green `OCR` badge.

## Internal Assistant Behavior

The AI assistant is configured for internal DMV staff, not the public. It is intended to help with:

- application review
- identifying missing or inconsistent information
- processing decisions and escalation suggestions
- fraud-review interpretation in an operational tone

The prompt is defined in:

- [CarTitleOCR/Services/FoundryAgentService.cs](c:\Repos\dmvOCR\OCR\CarTitleOCR\Services\FoundryAgentService.cs)

## Current Limitations

- Fraud duplicate-VIN detection is in-memory only and resets when the app restarts
- OCR parsing is rule-based and tuned for the current sample formats
- Fraud detection is intentionally simple and should be treated as review assistance, not final adjudication

## Suggested Next Steps

- Persist fraud history to a database so duplicate VIN checks survive restarts
- Add test coverage for OCR parsing and fraud rules
- Add raw OCR diagnostics in development mode for parser troubleshooting
- Expand fraud rules to include cross-field consistency and repeat-actor patterns