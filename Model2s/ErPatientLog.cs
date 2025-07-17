using System;
using System.Collections.Generic;

namespace GrpcProduct.Model2s;

public partial class ErPatientLog
{
    public int Id { get; set; }

    public string? Hn { get; set; }

    public DateOnly Vstdate { get; set; }

    public TimeOnly Vsttime { get; set; }

    public string? Fname { get; set; }

    public string? Lname { get; set; }

    public string? Pname { get; set; }

    public TimeOnly? EnterErTime { get; set; }

    public string? EmergencyType { get; set; }

    public string? CurrentStatus { get; set; }

    public DateTime? StatusUpdatedAt { get; set; }

    public virtual ICollection<ErPatientStatusHistory> ErPatientStatusHistories { get; set; } = new List<ErPatientStatusHistory>();
}
