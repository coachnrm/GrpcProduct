using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace GrpcProduct.Model2s;

public partial class ErdatabaseContext : DbContext
{
    public ErdatabaseContext()
    {
    }

    public ErdatabaseContext(DbContextOptions<ErdatabaseContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ErPatientLog> ErPatientLogs { get; set; }

    public virtual DbSet<ErPatientStatusHistory> ErPatientStatusHistories { get; set; }

    public virtual DbSet<ErStatus> ErStatuses { get; set; }

//     protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
// #warning To protect potentially sensitive information in your connection string, you should move it out of source code. You can avoid scaffolding the connection string by using the Name= syntax to read it from configuration - see https://go.microsoft.com/fwlink/?linkid=2131148. For more guidance on storing connection strings, see https://go.microsoft.com/fwlink/?LinkId=723263.
//         => optionsBuilder.UseSqlServer("Server=172.16.200.202,1434;Database=ERDatabase;Trusted_Connection=false;TrustServerCertificate=true;User=sa;Password=reallyStrongPwd123;");

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ErPatientLog>(entity =>
        {
            entity.Property(e => e.Hn).HasColumnName("HN");
        });

        modelBuilder.Entity<ErPatientStatusHistory>(entity =>
        {
            entity.HasIndex(e => e.ErPatientLogId, "IX_ErPatientStatusHistories_ErPatientLogId");

            entity.HasOne(d => d.ErPatientLog).WithMany(p => p.ErPatientStatusHistories).HasForeignKey(d => d.ErPatientLogId);
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
