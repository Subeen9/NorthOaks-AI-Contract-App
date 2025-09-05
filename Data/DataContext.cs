using CMPS4110_NorthOaksProj.Models.Users;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace CMPS4110_NorthOaksProj.Data
{
    public sealed class DataContext : DbContext
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options) { }
        public DbSet<User> Users { get; set; }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataContext).GetTypeInfo().Assembly);
        }
    }
