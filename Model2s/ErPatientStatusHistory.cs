using System;
using System.Collections.Generic;

namespace GrpcProduct.Model2s;

public partial class ErPatientStatusHistory
{
    public int Id { get; set; }

    public int ErPatientLogId { get; set; }

    public string? Status { get; set; }

    public DateTime ChangedAt { get; set; }

    public virtual ErPatientLog ErPatientLog { get; set; } = null!;
}
