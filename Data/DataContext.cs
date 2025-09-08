using CMPS4110_NorthOaksProj.Models.Users;
using CMPS4110_NorthOaksProj.Models.Contracts;
using CMPS4110_NorthOaksProj.Models.Chat;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace CMPS4110_NorthOaksProj.Data
{
    public sealed class DataContext : IdentityDbContext<User, IdentityRole<int>, int>
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options) { }
        public DbSet<User> Users { get; set; }

        public DbSet<Contract> Contracts { get; set; }

        public DbSet<ChatSession> ChatSessions { get; set; } = null!;
        public DbSet<ChatMessage> ChatMessages { get; set; } = null!;
        public DbSet<ChatSessionContract> ChatSessionContracts { get; set; } = null!;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataContext).GetTypeInfo().Assembly);



            modelBuilder.Entity<ChatMessage>()
                .HasOne(m => m.Session)
                .WithMany(s => s.Messages)
                .HasForeignKey(m => m.SessionId)
                .OnDelete(DeleteBehavior.Cascade);

    
            modelBuilder.Entity<ChatSessionContract>()
                .HasOne(sc => sc.ChatSession)
                .WithMany(s => s.SessionContracts)
                .HasForeignKey(sc => sc.ChatSessionId)
                .OnDelete(DeleteBehavior.Cascade);

          
            modelBuilder.Entity<ChatSessionContract>()
                .HasOne<Contract>()                
                .WithMany()                        
                .HasForeignKey(sc => sc.ContractId)
                .OnDelete(DeleteBehavior.Restrict);

            
            modelBuilder.Entity<ChatSessionContract>()
                .HasIndex(sc => new { sc.ChatSessionId, sc.ContractId })
                .IsUnique();
        }
    }
}
// ask aakash one to many 