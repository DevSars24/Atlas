using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atlas.Core.Interfaces;

public interface IJobHandler<TPayload>
{
    Task ExecuteAsync(Guid jobId, TPayload payload, CancellationToken cancellationToken);
}
