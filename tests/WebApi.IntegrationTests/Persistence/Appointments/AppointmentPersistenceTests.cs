using Ardalis.Specification.EntityFrameworkCore;
using MedicalApp.Common.Domain.Appointments;
using MedicalApp.Common.Persistence;
using MedicalApp.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace MedicalApp.IntegrationTests.Persistence.Appointments;

public class AppointmentPersistenceTests : IntegrationTestBase
{
    private readonly TestingDatabaseFixture _fixture;

    public AppointmentPersistenceTests(TestingDatabaseFixture fixture) : base(fixture)
    {
        _fixture = fixture;
    }

    private async Task<Appointment> CreateAppointmentAsync()
    {
        var patient = Patient.Create("Jane Doe", "jane@example.com", "555-123-4567");
        var doctor = Doctor.Create("Dr. Smith", "Cardiology");
        await AddAsync(patient);
        await AddAsync(doctor);
        var start = new DateTime(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);
        var appointment = Appointment.Create(patient.Id, doctor.Id, start, start.AddMinutes(30), "Initial notes");
        await AddAsync(appointment);
        return appointment;
    }

    [Fact]
    public async Task Save_ShouldPersistRelationshipsAuditAndUtcTimes()
    {
        var appointment = await CreateAppointmentAsync();
        using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var saved = await db.Appointments.WithSpecification(AppointmentSpec.ById(appointment.Id))
            .SingleAsync(CancellationToken);

        saved.Patient.FullName.Should().Be("Jane Doe");
        saved.Doctor.Specialty.Should().Be("Cardiology");
        saved.StartUtc.Should().Be(appointment.StartUtc);
        saved.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
        saved.EndUtc.Kind.Should().Be(DateTimeKind.Utc);
        saved.CreatedBy.Should().NotBeNullOrWhiteSpace();
        saved.CreatedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
        saved.RowVersion.Should().HaveCount(8);
        saved.PopDomainEvents().Should().BeEmpty();

        saved.Complete("Completion notes", saved.EndUtc);
        await db.SaveChangesAsync(CancellationToken);
        db.ChangeTracker.Clear();

        var completed = await db.Appointments.SingleAsync(a => a.Id == appointment.Id, CancellationToken);
        completed.Status.Should().Be(AppointmentStatus.Completed);
        completed.CompletedUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
        completed.CompletedUtc.Should().Be(appointment.EndUtc);
        completed.UpdatedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Cancel_ShouldPersistUtcTimestampAndReason()
    {
        var appointment = await CreateAppointmentAsync();
        appointment.Cancel("Unavailable", appointment.StartUtc.AddHours(-1));
        await SaveAsync();

        var saved = await GetQueryable<Appointment>().SingleAsync(a => a.Id == appointment.Id, CancellationToken);

        saved.Status.Should().Be(AppointmentStatus.Cancelled);
        saved.CancellationReason.Should().Be("Unavailable");
        saved.CancelledUtc!.Value.Kind.Should().Be(DateTimeKind.Utc);
        saved.CancelledUtc.Should().Be(appointment.CancelledUtc);
    }

    [Fact]
    public async Task Save_WithStaleRowVersion_ShouldRejectConcurrentUpdate()
    {
        var appointment = await CreateAppointmentAsync();
        using var firstScope = _fixture.CreateScope();
        using var secondScope = _fixture.CreateScope();
        var firstDb = firstScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var secondDb = secondScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var first = await firstDb.Appointments.SingleAsync(a => a.Id == appointment.Id, CancellationToken);
        var second = await secondDb.Appointments.SingleAsync(a => a.Id == appointment.Id, CancellationToken);

        first.Complete("First update");
        await firstDb.SaveChangesAsync(CancellationToken);
        second.Cancel("Concurrent cancellation");
        Func<Task> save = () => secondDb.SaveChangesAsync(CancellationToken);

        await save.Should().ThrowAsync<DbUpdateConcurrencyException>();
    }
}
