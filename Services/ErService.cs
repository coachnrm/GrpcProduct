using System;
using System.Threading.Channels;

// using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcErService;
using GrpcProduct.Model2s;
using Microsoft.EntityFrameworkCore;

namespace GrpcProduct.Services;

public class ErService : Er.ErBase
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Channel<ErPatient> _updateChannel;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);

    public ErService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _updateChannel = Channel.CreateUnbounded<ErPatient>();
        _ = WatchForChangesAsync();
    }

    public override async Task GetErPatientsStream(GetErPatientsRequest request, 
        IServerStreamWriter<ErPatientUpdate> responseStream, 
        ServerCallContext context)
    {
        try
        {
            // First send the current state
            var initialPatients = await GetCurrentErPatientsAsync();
            await responseStream.WriteAsync(new ErPatientUpdate
            {
                InitialBatch = initialPatients
            });

            // Then listen for updates
            await foreach (var patient in _updateChannel.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(new ErPatientUpdate
                {
                    UpdatedPatient = patient,
                    UpdateType = ErPatientUpdate.Types.UpdateType.Updated
                });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in patient stream: {ex.Message}");
            throw new RpcException(new Status(StatusCode.Internal, "Error streaming patient updates"));
        }
    }

    public override async Task<GetErPatientsResponse> GetErPatients(GetErPatientsRequest request, ServerCallContext context)
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErdatabaseContext>();

        try
        {
            var today = DateTime.Today;
            var yesterdayNight = today.AddDays(-1).AddHours(21);
            var tomorrow = today.AddDays(1);

            var rawData = await dbContext.ErPatientLogs
                .Where(log => log.StatusUpdatedAt >= yesterdayNight && log.StatusUpdatedAt < tomorrow)
                .ToListAsync();

            var latestPerHN = rawData
                .GroupBy(log => log.Hn)
                .Select(g => g.OrderByDescending(log => log.StatusUpdatedAt).First())
                .Select(log => new ErPatient
                {
                    Id = log.Id,
                    Hn = log.Hn ?? "",
                    Fname = log.Fname ?? "",
                    Lname = log.Lname ?? "",
                    Pname = log.Pname ?? "",
                    EnterErTime = FormatTimeOnly(log.EnterErTime),
                    EmergencyType = log.EmergencyType ?? "",
                    CurrentStatus = log.CurrentStatus ?? "",
                    StatusUpdatedAt = (log.StatusUpdatedAt ?? DateTime.Now).ToString("o")
                })
                .ToList();

            var response = new GetErPatientsResponse();
            response.Patients.AddRange(latestPerHN);
            return response;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving ER patients: {ex.Message}");
            throw new RpcException(new Status(StatusCode.Internal, "Error retrieving ER patients"));
        }
    }

    private async Task<GetErPatientsResponse> GetCurrentErPatientsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErdatabaseContext>();

        var today = DateTime.Today;
        var yesterdayNight = today.AddDays(-1).AddHours(21);
        var tomorrow = today.AddDays(1);

        var rawData = await dbContext.ErPatientLogs
            .Where(log => log.StatusUpdatedAt >= yesterdayNight && log.StatusUpdatedAt < tomorrow)
            .ToListAsync();

        var latestPerHN = rawData
            .GroupBy(log => log.Hn)
            .Select(g => g.OrderByDescending(log => log.StatusUpdatedAt).First())
            .Select(log => new ErPatient
            {
                Id = log.Id,
                Hn = log.Hn ?? "",
                Fname = log.Fname ?? "",
                Lname = log.Lname ?? "",
                Pname = log.Pname ?? "",
                EnterErTime = FormatTimeOnly(log.EnterErTime),
                EmergencyType = log.EmergencyType ?? "",
                CurrentStatus = log.CurrentStatus ?? "",
                StatusUpdatedAt = (log.StatusUpdatedAt ?? DateTime.Now).ToString("o")
            })
            .ToList();

        var response = new GetErPatientsResponse();
        response.Patients.AddRange(latestPerHN);
        return response;
    }

    private async Task WatchForChangesAsync()
    {
        while (!Environment.HasShutdownStarted)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ErdatabaseContext>();

                var lastCheck = await dbContext.ErPatientLogs
                    .OrderByDescending(log => log.StatusUpdatedAt)
                    .Select(log => log.StatusUpdatedAt)
                    .FirstOrDefaultAsync();

                var changes = await dbContext.ErPatientLogs
                    .Where(log => log.StatusUpdatedAt >= lastCheck)
                    .ToListAsync();

                if (changes.Any())
                {
                    foreach (var change in changes)
                    {
                        var patient = new ErPatient
                        {
                            Id = change.Id,
                            Hn = change.Hn ?? "",
                            Fname = change.Fname ?? "",
                            Lname = change.Lname ?? "",
                            Pname = change.Pname ?? "",
                            EnterErTime = FormatTimeOnly(change.EnterErTime),
                            EmergencyType = change.EmergencyType ?? "",
                            CurrentStatus = change.CurrentStatus ?? "",
                            StatusUpdatedAt = (change.StatusUpdatedAt ?? DateTime.Now).ToString("o")
                        };

                        await _updateChannel.Writer.WriteAsync(patient);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Graceful shutdown
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error watching for changes: {ex.Message}");
            }

            await Task.Delay(_pollingInterval);
        }
    }

    private static string FormatTimeOnly(TimeOnly? time)
    {
        return time?.ToString("HH':'mm':'ss") ?? "00:00:00";
    }
}
