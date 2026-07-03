using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Interfaces;
using Microsoft.Extensions.Logging;

namespace Atlas.Worker.Jobs;

public class SendEmailPayload
{
    public string To { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public bool SimulateFailure { get; set; } = false;
}

/// <summary>
/// Demo job handler: simulates sending an email.
/// </summary>
public class SendEmailJobHandler : IJobHandler<SendEmailPayload>
{
    private readonly ILogger<SendEmailJobHandler> _logger;

    public SendEmailJobHandler(ILogger<SendEmailJobHandler> logger)
    {
        _logger = logger;
    }

    public async Task ExecuteAsync(Guid jobId, SendEmailPayload payload, CancellationToken cancellationToken)
    {
        _logger.LogInformation("[SendEmailJob] JobId={JobId} — Preparing email to {To}", jobId, payload.To);

        await Task.Delay(500, cancellationToken);

        if (payload.SimulateFailure)
            throw new InvalidOperationException($"Simulated SMTP failure for {payload.To}");

        _logger.LogInformation("[SendEmailJob] JobId={JobId} — Connecting to SMTP...", jobId);
        await Task.Delay(300, cancellationToken);

        _logger.LogInformation("[SendEmailJob] JobId={JobId} — Sending: Subject='{Subject}' To={To}", jobId, payload.Subject, payload.To);
        await Task.Delay(400, cancellationToken);

        _logger.LogInformation("[SendEmailJob] JobId={JobId} — Email delivered successfully.", jobId);
    }
}
