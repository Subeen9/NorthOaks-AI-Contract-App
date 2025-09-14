using CMPS4110_NorthOaksProj.Models.Chat;
using CMPS4110_NorthOaksProj.Models.Users;
using CMPS4110_NorthOaksProj.Models.Contracts;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;

namespace CMPS4110_NorthOaksProj.Data
{
    public sealed class DataContext : IdentityDbContext<User, IdentityRole<int>, int>
    {
        public DataContext(DbContextOptions<DataContext> options) : base(options) { }
        public DbSet<User> Users { get; set; }
        public DbSet<Contract> Contracts { get; set; }
        public DbSet<ContractEmbedding> ContractEmbeddings { get; set; }
        public DbSet<ChatSession> ChatSessions { get; set; } = null!;
        public DbSet<ChatMessage> ChatMessages { get; set; } = null!;
        public DbSet<ChatSessionContract> ChatSessionContracts { get; set; } = null!;
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            //Contract Embeddings Relation mapping
            modelBuilder.Entity<ContractEmbedding>()
                .HasOne(ce=> ce.Contract)
                .WithMany()
                .HasForeignKey(ce=>ce.ContractId)
                .OnDelete(DeleteBehavior.Cascade);

            //Contract Embeddings Indexed on ContractId
            modelBuilder.Entity<ContractEmbedding>()
                .HasIndex(ce => ce.ContractId);

            modelBuilder.ApplyConfigurationsFromAssembly(typeof(DataContext).GetTypeInfo().Assembly);
        }
    }
}
