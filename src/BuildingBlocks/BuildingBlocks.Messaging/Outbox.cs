using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace BuildingBlocks.Messaging;

/// <summary>
/// An event waiting to be published. It is inserted in the same database transaction as the
/// business change, so either both are committed or neither is ("transactional outbox").
/// </summary>
public sealed class OutboxMessage
{
    public Guid Id { get; set; }
    public string Type { get; set; } = "";
    public string Payload { get; set; } = "";
    public DateTimeOffset OccurredAt { get; set; }
    public string? TraceParent { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
    public int Attempts { get; set; }
    public string? LastError { get; set; }

    internal static OutboxMessage From<TEvent>(TEvent @event) where TEvent : IntegrationEvent => new()
    {
        Id = @event.Id,
        Type = EventNames.Of(typeof(TEvent)),
        Payload = JsonSerializer.Serialize(@event, RabbitMqTopology.Json),
        OccurredAt = @event.OccurredAt,
        TraceParent = Activity.Current?.Id,   // lets the trace continue when the relay publishes later
    };
}

public static class OutboxExtensions
{
    /// <summary>Maps the outbox table. Call from <c>OnModelCreating</c> and add a migration.</summary>
    public static ModelBuilder AddOutbox(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<OutboxMessage>(outbox =>
        {
            // Explicit snake_case names: the relay queries this table with raw SQL.
            outbox.ToTable("outbox_messages");
            outbox.HasKey(m => m.Id);
            outbox.Property(m => m.Id).HasColumnName("id");
            outbox.Property(m => m.Type).HasColumnName("type").HasMaxLength(200);
            outbox.Property(m => m.Payload).HasColumnName("payload");
            outbox.Property(m => m.OccurredAt).HasColumnName("occurred_at");
            outbox.Property(m => m.TraceParent).HasColumnName("trace_parent").HasMaxLength(64);
            outbox.Property(m => m.ProcessedAt).HasColumnName("processed_at");
            outbox.Property(m => m.Attempts).HasColumnName("attempts");
            outbox.Property(m => m.LastError).HasColumnName("last_error").HasMaxLength(1000);
            outbox.HasIndex(m => m.OccurredAt).HasFilter("processed_at IS NULL");
        });
        return modelBuilder;
    }

    /// <summary>Stages an event; it is written when the caller calls <c>SaveChanges</c>.</summary>
    public static void AddOutboxMessage<TEvent>(this DbContext db, TEvent @event) where TEvent : IntegrationEvent =>
        db.Set<OutboxMessage>().Add(OutboxMessage.From(@event));
}

/// <summary>
/// Polls the outbox and publishes pending messages in order. Rows are claimed with
/// <c>FOR UPDATE SKIP LOCKED</c>, so several replicas can run the relay without double-publishing
/// the same batch. Delivery is at-least-once: consumers must be idempotent.
/// </summary>
internal sealed class OutboxRelay<TDbContext>(
    IServiceScopeFactory scopes,
    RabbitMqPublisher publisher,
    ILogger<OutboxRelay<TDbContext>> logger) : BackgroundService where TDbContext : DbContext
{
    private const int BatchSize = 50;
    private const string ClaimBatchSql = """
        SELECT * FROM outbox_messages
        WHERE processed_at IS NULL
        ORDER BY occurred_at
        LIMIT 50
        FOR UPDATE SKIP LOCKED
        """;   // LIMIT must match BatchSize
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        do
        {
            try
            {
                while (await PublishBatchAsync(stoppingToken) == BatchSize) { }
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogWarning(ex, "Outbox relay iteration failed; retrying in {Interval}", PollInterval);
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task<int> PublishBatchAsync(CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<TDbContext>();

        // The Aspire Npgsql integration enables retries, which require explicit transactions to run inside the strategy.
        return await db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);

            var batch = await db.Set<OutboxMessage>()
                .FromSqlRaw(ClaimBatchSql)
                .ToListAsync(ct);

            foreach (var message in batch)
            {
                try
                {
                    ActivityContext.TryParse(message.TraceParent, null, out var parent);
                    await publisher.PublishRawAsync(message.Type, message.Id, System.Text.Encoding.UTF8.GetBytes(message.Payload), parent, ct);
                    message.ProcessedAt = DateTimeOffset.UtcNow;
                }
                catch (Exception ex) when (!ct.IsCancellationRequested)
                {
                    message.Attempts++;
                    message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;
                    logger.LogWarning(ex, "Publishing outbox message {MessageId} ({Type}) failed", message.Id, message.Type);
                    break;   // keep ordering: stop at the first failure and retry on the next tick
                }
            }

            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return batch.Count;
        });
    }
}
