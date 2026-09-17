using ITBees.Printers.DbModels;
using Microsoft.EntityFrameworkCore;

namespace ITBees.Printers.Setup;

public class DbModelBuilder
{
    /// <summary>
    /// Registers every entity shipped with this package. Call it from the consuming
    /// DbContext's OnModelCreating and create the matching migration.
    /// </summary>
    public static void Register(ModelBuilder modelBuilder)
    {
        // Agents, settings and jobs are personal data of one account - they go away with it.
        modelBuilder.Entity<PrintAgent>().HasKey(x => x.Guid);
        modelBuilder.Entity<PrintAgent>().Property(x => x.MachineName).HasMaxLength(200);
        modelBuilder.Entity<PrintAgent>().Property(x => x.AgentVersion).HasMaxLength(50);
        modelBuilder.Entity<PrintAgent>().Property(x => x.OsVersion).HasMaxLength(200);
        modelBuilder.Entity<PrintAgent>().Property(x => x.TokenHash).HasMaxLength(64);
        modelBuilder.Entity<PrintAgent>()
            .HasOne(x => x.UserAccount)
            .WithMany()
            .HasForeignKey(x => x.UserAccountGuid)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PrintAgentPrinter>().HasKey(x => x.Guid);
        modelBuilder.Entity<PrintAgentPrinter>().Property(x => x.Name).HasMaxLength(PrintAgentPrinter.MaxNameLength);
        modelBuilder.Entity<PrintAgentPrinter>().Property(x => x.DriverName).HasMaxLength(260);
        modelBuilder.Entity<PrintAgentPrinter>().Property(x => x.PortName).HasMaxLength(260);
        modelBuilder.Entity<PrintAgentPrinter>().Property(x => x.Location).HasMaxLength(260);
        modelBuilder.Entity<PrintAgentPrinter>().Property(x => x.Status).HasMaxLength(100);
        // A job is addressed by the printer's system name, so it must be unique within an agent.
        modelBuilder.Entity<PrintAgentPrinter>().HasIndex(x => new { x.PrintAgentGuid, x.Name }).IsUnique();
        modelBuilder.Entity<PrintAgentPrinter>()
            .HasOne(x => x.PrintAgent)
            .WithMany(x => x.Printers)
            .HasForeignKey(x => x.PrintAgentGuid)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<UserPrintSetting>().HasKey(x => x.Guid);
        modelBuilder.Entity<UserPrintSetting>().Property(x => x.DocumentType).HasMaxLength(PrintDocumentType.MaxKeyLength);
        // One row per user and document type - this is what keeps the settings personal.
        modelBuilder.Entity<UserPrintSetting>().HasIndex(x => new { x.UserAccountGuid, x.DocumentType }).IsUnique();
        modelBuilder.Entity<UserPrintSetting>()
            .HasOne(x => x.UserAccount)
            .WithMany()
            .HasForeignKey(x => x.UserAccountGuid)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PrintJob>().HasKey(x => x.Guid);
        modelBuilder.Entity<PrintJob>().Property(x => x.DocumentType).HasMaxLength(PrintDocumentType.MaxKeyLength);
        modelBuilder.Entity<PrintJob>().Property(x => x.DocumentName).HasMaxLength(PrintJob.MaxDocumentNameLength);
        modelBuilder.Entity<PrintJob>().Property(x => x.PrinterName).HasMaxLength(PrintAgentPrinter.MaxNameLength);
        modelBuilder.Entity<PrintJob>().Property(x => x.AgentMachineName).HasMaxLength(200);
        modelBuilder.Entity<PrintJob>().Property(x => x.StatusMessage).HasMaxLength(PrintJob.MaxStatusMessageLength);
        // Pruned by age; read by guid (status polling) and per user.
        modelBuilder.Entity<PrintJob>().HasIndex(x => x.Created);
        modelBuilder.Entity<PrintJob>()
            .HasOne(x => x.UserAccount)
            .WithMany()
            .HasForeignKey(x => x.UserAccountGuid)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
