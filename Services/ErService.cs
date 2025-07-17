using System;
// using Google.Protobuf.WellKnownTypes;
using Grpc.Core;
using GrpcErService;
using GrpcProduct.Model2s;
using Microsoft.EntityFrameworkCore;

namespace GrpcProduct.Services;

public class ErService : Er.ErBase
{
    private readonly ErdatabaseContext _context;

    public ErService(ErdatabaseContext context)
    {
        _context = context;
    }

    public override async Task<GetErPatientsResponse> GetErPatients(GetErPatientsRequest request, ServerCallContext context)
    {
        try
        {
            var today = DateTime.Today; // Today 00:00
            var yesterdayNight = today.AddDays(-1).AddHours(21); // Yesterday 21:00
            var tomorrow = today.AddDays(1); // Tomorrow 00:00

            // 1. Get data from today and yesterday between 21:00-23:59
            var rawData = await _context.ErPatientLogs
                .Where(log =>
                    (log.StatusUpdatedAt >= yesterdayNight && log.StatusUpdatedAt < tomorrow)
                )
                .ToListAsync();

            // 2. Filter duplicate HNs by selecting the latest updated record
            var latestPerHN = rawData
                .GroupBy(log => log.Hn)
                .Select(g => g
                    .OrderByDescending(log => log.StatusUpdatedAt)
                    .First())
                .Select(log => new ErPatient
                {
                    Id = log.Id,
                    Hn = log.Hn ?? "",
                    Fname = log.Fname ?? "",
                    Lname = log.Lname ?? "",
                    Pname = log.Pname ?? "",
                    EnterErTime = FormatTimeOnly(log.EnterErTime), // Fixed time format
                    EmergencyType = log.EmergencyType ?? "",
                    CurrentStatus = log.CurrentStatus ?? "",
                    StatusUpdatedAt = (log.StatusUpdatedAt ?? DateTime.Now).ToString("o") // ISO 8601 format
                })
                .ToList();

            var response = new GetErPatientsResponse();
            response.Patients.AddRange(latestPerHN);

            return response;
        }
        catch (Exception ex)
        {
            // Log the exception
            Console.WriteLine($"Error retrieving ER patients: {ex.Message}");
            throw new RpcException(new Status(StatusCode.Internal, "Error retrieving ER patients"));
        }
    }

    private string FormatTimeOnly(TimeOnly? time)
    {
        if (!time.HasValue)
        {
            return "00:00:00"; // Default value if null
        }
        return time.Value.ToString("HH':'mm':'ss");
    }
}
