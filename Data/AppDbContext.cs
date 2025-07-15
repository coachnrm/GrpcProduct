using System;
using GrpcProduct.Models;
using Microsoft.EntityFrameworkCore;

namespace GrpcProduct.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) {}

    public DbSet<Products> Products => Set<Products>();
}
