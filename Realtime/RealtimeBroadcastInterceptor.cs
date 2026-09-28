using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using PortfolioManager.Api.Dtos;
using PortfolioManager.Api.Models;

namespace PortfolioManager.Api.Realtime;

/// <summary>
/// Pushes newly inserted assets and prices (and updated prices) to WebSocket clients, but only after
/// SaveChanges has succeeded (so generated IDs are populated and nothing is sent for a rollback).
/// </summary>
public sealed class RealtimeBroadcastInterceptor(WebSocketHub hub) : SaveChangesInterceptor
{
    // Entities added or modified in the save currently in progress, per DbContext instance.
    private readonly ConditionalWeakTable<DbContext, List<(object Entity, EntityState State)>> _pending = new();

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        Capture(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        Capture(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        Publish(eventData.Context);
        return result;
    }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Publish(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => Discard(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        Discard(eventData.Context);
        return Task.CompletedTask;
    }

    private void Capture(DbContext? context)
    {
        if (context is null)
            return;

        var changed = context.ChangeTracker.Entries()
            .Where(e => (e.State == EntityState.Added && e.Entity is Asset or Price)
                || (e.State == EntityState.Modified && e.Entity is Price))
            .Select(e => (e.Entity, e.State))
            .ToList();

        if (changed.Count > 0)
            _pending.AddOrUpdate(context, changed);
    }

    private void Publish(DbContext? context)
    {
        if (context is null || !_pending.TryGetValue(context, out var changed))
            return;

        _pending.Remove(context);
        foreach (var (entity, state) in changed)
        {
            switch (entity)
            {
                case Asset a:
                    hub.Publish("asset.created", new AssetDto(a.Id, a.Symbol, a.Identifier, a.Name, a.Type, a.CreatedAt));
                    break;
                case Price p:
                    hub.Publish(state == EntityState.Added ? "price.created" : "price.updated",
                        new PriceDto(p.Id, p.AssetId, p.Value, p.Date, p.Nav));
                    break;
            }
        }
    }

    private void Discard(DbContext? context)
    {
        if (context is not null)
            _pending.Remove(context);
    }
}
