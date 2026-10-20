using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using SaasStarter.Infrastructure.Email;

namespace SaasStarter.Infrastructure.Jobs;

/// <summary>
/// A unit of background work. Jobs run outside the request, in their own DI
/// scope (created by QueuedHostedService), so they can safely use scoped
/// services like AppDbContext and IEmailSender. Keep jobs idempotent —
/// production systems swap this queue for Hangfire/Azure Queue Storage
/// without changing the jobs themselves.
/// </summary>
public interface IBackgroundJob
{
    Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken);
}

public interface IBackgroundJobQueue
{
    ValueTask EnqueueAsync(IBackgroundJob job, CancellationToken cancellationToken = default);
    ValueTask<IBackgroundJob> DequeueAsync(CancellationToken cancellationToken);
}

/// <summary>In-process FIFO queue backed by a Channel. Simple, dependency-free, testable.</summary>
public class BackgroundJobQueue : IBackgroundJobQueue
{
    private readonly Channel<IBackgroundJob> _queue = Channel.CreateUnbounded<IBackgroundJob>();

    public ValueTask EnqueueAsync(IBackgroundJob job, CancellationToken cancellationToken = default)
        => _queue.Writer.WriteAsync(job, cancellationToken);

    public ValueTask<IBackgroundJob> DequeueAsync(CancellationToken cancellationToken)
        => _queue.Reader.ReadAsync(cancellationToken);
}

public class QueuedHostedService : BackgroundService
{
    private readonly IBackgroundJobQueue _queue;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<QueuedHostedService> _logger;

    public QueuedHostedService(
        IBackgroundJobQueue queue,
        IServiceScopeFactory scopeFactory,
        ILogger<QueuedHostedService> logger)
    {
        _queue = queue;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Background job queue started");
        while (!stoppingToken.IsCancellationRequested)
        {
            IBackgroundJob job;
            try
            {
                job = await _queue.DequeueAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                await job.ExecuteAsync(scope.ServiceProvider, stoppingToken);
            }
            catch (Exception ex)
            {
                // A failed job must never kill the queue loop.
                _logger.LogError(ex, "Background job {Job} failed", job.GetType().Name);
            }
        }
    }
}

/// <summary>Sends one email in the background — the welcome email on signup is the first user.</summary>
public record SendEmailJob(string To, string Subject, string HtmlBody) : IBackgroundJob
{
    public async Task ExecuteAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var sender = services.GetRequiredService<IEmailSender>();
        await sender.SendAsync(To, Subject, HtmlBody, cancellationToken);
    }
}
