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
    private readonly Channel<ErPatientUpdate> _updateChannel;
    private readonly TimeSpan _pollingInterval = TimeSpan.FromSeconds(5);
    private Dictionary<string, DateTime> _lastKnownPatients = new();

    public ErService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _updateChannel = Channel.CreateUnbounded<ErPatientUpdate>();
        _ = InitializeLastKnownPatientsAsync();
        _ = WatchForChangesAsync();
    }

    private async Task InitializeLastKnownPatientsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErdatabaseContext>();

        var today = DateTime.Today;
        var yesterdayNight = today.AddDays(-1).AddHours(21);
        var tomorrow = today.AddDays(1);

        var patients = await dbContext.ErPatientLogs
            .Where(log => log.StatusUpdatedAt >= yesterdayNight && log.StatusUpdatedAt < tomorrow)
            .ToListAsync();

        _lastKnownPatients = patients
            .GroupBy(p => p.Hn)
            .ToDictionary(
                g => g.Key,
                g => g.Max(p => p.StatusUpdatedAt) ?? DateTime.MinValue);
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
                InitialBatch = initialPatients,
                UpdateType = ErPatientUpdate.Types.UpdateType.Unknown
            });

            // Then listen for updates
            await foreach (var update in _updateChannel.Reader.ReadAllAsync(context.CancellationToken))
            {
                await responseStream.WriteAsync(update);
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
        var response = await GetCurrentErPatientsAsync();
        return response;
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

                var today = DateTime.Today;
                var yesterdayNight = today.AddDays(-1).AddHours(21);
                var tomorrow = today.AddDays(1);

                // Get current state
                var currentPatients = await dbContext.ErPatientLogs
                    .Where(log => log.StatusUpdatedAt >= yesterdayNight && log.StatusUpdatedAt < tomorrow)
                    .ToListAsync();

                var currentGroups = currentPatients
                    .GroupBy(p => p.Hn)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Max(p => p.StatusUpdatedAt) ?? DateTime.MinValue);

                // Detect deletions
                var deletedHNs = _lastKnownPatients.Keys.Except(currentGroups.Keys).ToList();
                foreach (var hn in deletedHNs)
                {
                    await _updateChannel.Writer.WriteAsync(new ErPatientUpdate
                    {
                        UpdatedPatient = new ErPatient { Hn = hn },
                        UpdateType = ErPatientUpdate.Types.UpdateType.Deleted
                    });
                }

                // Detect new insertions and updates
                foreach (var currentGroup in currentGroups)
                {
                    if (!_lastKnownPatients.TryGetValue(currentGroup.Key, out var lastUpdateTime))
                    {
                        // New patient
                        var patient = currentPatients
                            .Where(p => p.Hn == currentGroup.Key)
                            .OrderByDescending(p => p.StatusUpdatedAt)
                            .First();

                        await _updateChannel.Writer.WriteAsync(new ErPatientUpdate
                        {
                            UpdatedPatient = ConvertToGrpcPatient(patient),
                            UpdateType = ErPatientUpdate.Types.UpdateType.Inserted
                        });
                    }
                    else if (currentGroup.Value > lastUpdateTime)
                    {
                        // Updated patient
                        var patient = currentPatients
                            .Where(p => p.Hn == currentGroup.Key)
                            .OrderByDescending(p => p.StatusUpdatedAt)
                            .First();

                        await _updateChannel.Writer.WriteAsync(new ErPatientUpdate
                        {
                            UpdatedPatient = ConvertToGrpcPatient(patient),
                            UpdateType = ErPatientUpdate.Types.UpdateType.Updated
                        });
                    }
                }

                // Update our last known state
                _lastKnownPatients = currentGroups;
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

    private ErPatient ConvertToGrpcPatient(ErPatientLog patientLog)
    {
        return new ErPatient
        {
            Id = patientLog.Id,
            Hn = patientLog.Hn ?? "",
            Fname = patientLog.Fname ?? "",
            Lname = patientLog.Lname ?? "",
            Pname = patientLog.Pname ?? "",
            EnterErTime = FormatTimeOnly(patientLog.EnterErTime),
            EmergencyType = patientLog.EmergencyType ?? "",
            CurrentStatus = patientLog.CurrentStatus ?? "",
            StatusUpdatedAt = (patientLog.StatusUpdatedAt ?? DateTime.Now).ToString("o")
        };
    }

    private static string FormatTimeOnly(TimeOnly? time)
    {
        return time?.ToString("HH':'mm':'ss") ?? "00:00:00";
    }
    
    public override async Task GetErStatusCountStream(GetErStatusCountRequest request, 
    IServerStreamWriter<ErStatusCountUpdate> responseStream, 
    ServerCallContext context)
    {
        try
        {
            // First send the current state
            var initialCounts = await GetCurrentStatusCountsAsync();
            await responseStream.WriteAsync(new ErStatusCountUpdate
            {
                StatusCounts = { initialCounts }
            });

            // Then listen for updates
            var lastSentCounts = initialCounts;
            while (!context.CancellationToken.IsCancellationRequested)
            {
                await Task.Delay(_pollingInterval, context.CancellationToken);
                
                var currentCounts = await GetCurrentStatusCountsAsync();
                
                // Only send if counts have changed
                if (!CountsEqual(lastSentCounts, currentCounts))
                {
                    await responseStream.WriteAsync(new ErStatusCountUpdate
                    {
                        StatusCounts = { currentCounts }
                    });
                    lastSentCounts = currentCounts;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error in status count stream: {ex.Message}");
            throw new RpcException(new Status(StatusCode.Internal, "Error streaming status counts"));
        }
    }

    private async Task<Dictionary<string, int>> GetCurrentStatusCountsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<ErdatabaseContext>();

        var today = DateTime.Today;
        var yesterdayNight = today.AddDays(-1).AddHours(21);
        var tomorrow = today.AddDays(1);

        // Get raw status counts
        var rawStatusCounts = await dbContext.ErPatientLogs
            .Where(p => p.StatusUpdatedAt >= yesterdayNight && p.StatusUpdatedAt < tomorrow)
            .GroupBy(p => p.CurrentStatus)
            .Select(g => new
            {
                Status = g.Key,
                Count = g.Count()
            })
            .ToListAsync();

        // Convert to dictionary
        var rawDict = rawStatusCounts
            .ToDictionary(
                x => string.IsNullOrWhiteSpace(x.Status) ? "ไม่ทราบสถานะ" : x.Status.Trim(),
                x => x.Count
            );

        // Group status counts by category
        var groupedStatusCounts = new Dictionary<string, int>
        {
            ["ตรวจเสร็จแล้ว"] = SumStatus(rawDict, new[] { "กลับบ้าน", "ส่งต่อ ร.พ. อื่น", "ส่งต่อ OPD", "ส่งนอน ร.พ." }),
            ["กำลังให้การรักษา"] = SumStatus(rawDict, new[] { "รอผลตรวจ LAB", "กำลังให้การรักษา", "รอสังเกตุอาการ" }),
            ["รอตรวจ"] = SumStatus(rawDict, new[] { "รอตรวจ", "รอพบแพทย์" })
        };

        return groupedStatusCounts;
    }

    private int SumStatus(Dictionary<string, int> dict, string[] keys)
    {
        return keys.Sum(k => dict.TryGetValue(k, out var count) ? count : 0);
    }

    private bool CountsEqual(Dictionary<string, int> a, Dictionary<string, int> b)
    {
        if (a.Count != b.Count) return false;
        
        foreach (var kvp in a)
        {
            if (!b.TryGetValue(kvp.Key, out var value) || value != kvp.Value)
            {
                return false;
            }
        }
        
        return true;
    }
}
