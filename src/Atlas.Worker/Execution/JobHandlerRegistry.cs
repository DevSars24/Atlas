using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Atlas.Core.Interfaces;
using Microsoft.Extensions.DependencyInjection;

namespace Atlas.Worker.Execution;

public interface IJobHandlerRegistry
{
    void RegisterHandler<THandler, TPayload>(string jobType) where THandler : class, IJobHandler<TPayload>;
    Task ExecuteJobAsync(IServiceProvider serviceProvider, Guid jobId, string jobType, string payloadJson, CancellationToken cancellationToken);
    RetryOptions GetRetryOptions(string jobType);
    void RegisterRetryOptions(string jobType, RetryOptions options);
    IEnumerable<string> GetRegisteredJobTypes();
}

public class JobHandlerRegistry : IJobHandlerRegistry
{
    private readonly Dictionary<string, Type> _payloadTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Type> _handlerTypes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RetryOptions> _retryOptions = new(StringComparer.OrdinalIgnoreCase);
    private readonly RetryOptions _defaultRetryOptions = new();

    public void RegisterHandler<THandler, TPayload>(string jobType) where THandler : class, IJobHandler<TPayload>
    {
        _handlerTypes[jobType] = typeof(THandler);
        _payloadTypes[jobType] = typeof(TPayload);
    }

    public void RegisterRetryOptions(string jobType, RetryOptions options)
    {
        _retryOptions[jobType] = options;
    }

    public RetryOptions GetRetryOptions(string jobType)
    {
        return _retryOptions.TryGetValue(jobType, out var options) ? options : _defaultRetryOptions;
    }

    public IEnumerable<string> GetRegisteredJobTypes()
    {
        return _handlerTypes.Keys;
    }

    public async Task ExecuteJobAsync(IServiceProvider serviceProvider, Guid jobId, string jobType, string payloadJson, CancellationToken cancellationToken)
    {
        if (!_handlerTypes.TryGetValue(jobType, out var handlerType) || !_payloadTypes.TryGetValue(jobType, out var payloadType))
        {
            throw new InvalidOperationException($"No handler registered for job type '{jobType}'");
        }

        var payload = JsonSerializer.Deserialize(payloadJson, payloadType, new JsonSerializerOptions 
        { 
            PropertyNameCaseInsensitive = true 
        });

        if (payload == null)
        {
            throw new InvalidOperationException($"Failed to deserialize payload for job type '{jobType}'");
        }

        using var scope = serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetRequiredService(handlerType);

        var method = handlerType.GetMethod(nameof(IJobHandler<object>.ExecuteAsync)) 
                     ?? throw new InvalidOperationException($"ExecuteAsync method not found on handler type '{handlerType.Name}'");

        var task = (Task)method.Invoke(handler, new[] { jobId, payload, cancellationToken })!;
        await task;
    }
}
