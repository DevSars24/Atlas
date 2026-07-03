using System;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Atlas.Worker.Jobs;

public class GenerateReportPayload
{
    public string ReportType { get; set; } = "Monthly";
    public string Format { get; set; } = "PDF";
    public string[] Recipients { get; set; } = Array.Empty<string>();
}

/// <summary>
/// Demo job handler: simulates generating a report and emailing it.
/// </summary>
public class GenerateReportJobHandler : IJobHandler<GenerateReportPayload>
{
    private readonly ILogger<GenerateReportJobHandler> _logger;

    public GenerateReportJobHandler(ILogger<GenerateReportJobHandler> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid jobId, GenerateReportPayload payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[GenerateReportJob] JobId={JobId} — Starting {Type} report generation ({Format})",
            jobId, payload.ReportType, payload.Format);

        _logger.LogInformation("[GenerateReportJob] JobId={JobId} — Querying data sources...", jobId);
        await Task.Delay(800, cancellationToken);

        _logger.LogInformation("[GenerateReportJob] JobId={JobId} — Rendering {Format} document...", jobId, payload.Format);
        await Task.Delay(1200, cancellationToken);

        _logger.LogInformation("[GenerateReportJob] JobId={JobId} — Uploading to storage...", jobId);
        await Task.Delay(400, cancellationToken);

        foreach (var recipient in payload.Recipients)
        {
            _logger.LogInformation("[GenerateReportJob] JobId={JobId} — Notifying {Recipient}", jobId, recipient);
            await Task.Delay(100, cancellationToken);
        }

        _logger.LogInformation("[GenerateReportJob] JobId={JobId} — Report completed and delivered to {Count} recipient(s).",
            jobId, payload.Recipients.Length);
    }
}
