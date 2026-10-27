using MedicalApp.Common.Domain.Appointments;
using DoctorId = MedicalApp.Common.Domain.Doctors.DoctorId;
using PatientId = MedicalApp.Common.Domain.Patients.PatientId;

namespace MedicalApp.UnitTests.Features.Appointments;

public class AppointmentTests
{
    private static readonly DateTime StartUtc = new(2026, 10, 20, 12, 0, 0, DateTimeKind.Utc);

    private static Appointment CreateAppointment(string? notes = null) => Appointment.Create(
        PatientId.From(Guid.CreateVersion7()), DoctorId.From(Guid.CreateVersion7()),
        StartUtc, StartUtc.AddMinutes(30), notes);

    [Fact]
    public void Create_ShouldGenerateIdAndQueueBookingEvent()
    {
        var appointment = CreateAppointment("  Initial notes  ");

        appointment.Id.Value.Should().NotBeEmpty();
        appointment.Id.Value.Version.Should().Be(7);
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.Notes.Should().Be("Initial notes");
        appointment.PopDomainEvents().Should().ContainSingle().Which.Should()
            .BeEquivalentTo(new AppointmentBookedEvent(appointment.Id, appointment.PatientId,
                appointment.DoctorId, appointment.StartUtc, appointment.EndUtc));
        appointment.PopDomainEvents().Should().BeEmpty();
    }

    [Theory]
    [InlineData(DateTimeKind.Local, DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified, DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Utc, DateTimeKind.Local)]
    [InlineData(DateTimeKind.Utc, DateTimeKind.Unspecified)]
    public void Create_WithNonUtcTime_ShouldThrow(DateTimeKind startKind, DateTimeKind endKind)
    {
        Action act = () => Appointment.Create(PatientId.From(Guid.CreateVersion7()),
            DoctorId.From(Guid.CreateVersion7()), DateTime.SpecifyKind(StartUtc, startKind),
            DateTime.SpecifyKind(StartUtc.AddMinutes(30), endKind));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-30)]
    [InlineData(9)]
    [InlineData(481)]
    public void Create_WithInvalidTimeRange_ShouldThrow(int minutes)
    {
        Action act = () => Appointment.Create(PatientId.From(Guid.CreateVersion7()),
            DoctorId.From(Guid.CreateVersion7()), StartUtc, StartUtc.AddMinutes(minutes));

        act.Should().Throw<ArgumentException>();
    }

    [Theory]
    [InlineData(10)]
    [InlineData(480)]
    public void Create_WithBoundaryDuration_ShouldSucceed(int minutes)
    {
        var appointment = Appointment.Create(PatientId.From(Guid.CreateVersion7()),
            DoctorId.From(Guid.CreateVersion7()), StartUtc, StartUtc.AddMinutes(minutes));

        (appointment.EndUtc - appointment.StartUtc).Should().Be(TimeSpan.FromMinutes(minutes));
    }

    [Fact]
    public void Complete_ShouldSetStateAndRaiseOneEvent_WhenCalledTwice()
    {
        var appointment = CreateAppointment();
        appointment.PopDomainEvents();
        var timestamp = StartUtc.AddHours(1);

        appointment.Complete("  Completed notes  ", timestamp);
        appointment.Complete("Other notes", timestamp.AddHours(1));

        appointment.Status.Should().Be(AppointmentStatus.Completed);
        appointment.CompletedUtc.Should().Be(timestamp);
        appointment.CancelledUtc.Should().BeNull();
        appointment.Notes.Should().Be("Completed notes");
        appointment.PopDomainEvents().Should().ContainSingle().Which.Should()
            .BeEquivalentTo(new AppointmentCompletedEvent(appointment.Id, appointment.PatientId,
                appointment.DoctorId, timestamp, "Completed notes"));
    }

    [Fact]
    public void Cancel_ShouldSetState_WhenCalledTwice()
    {
        var appointment = CreateAppointment();
        var timestamp = StartUtc.AddHours(-1);

        appointment.Cancel("Patient unavailable", timestamp);
        appointment.Cancel("Another reason", timestamp.AddMinutes(1));

        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
        appointment.CancelledUtc.Should().Be(timestamp);
        appointment.CancellationReason.Should().Be("Patient unavailable");
        appointment.CompletedUtc.Should().BeNull();
    }

    [Fact]
    public void CompletedAppointment_CannotBeCancelled()
    {
        var appointment = CreateAppointment();
        appointment.Complete();

        Action act = () => appointment.Cancel("Unavailable");

        act.Should().Throw<InvalidOperationException>();
        appointment.Status.Should().Be(AppointmentStatus.Completed);
    }

    [Fact]
    public void CancelledAppointment_CannotBeCompleted()
    {
        var appointment = CreateAppointment();
        appointment.Cancel("Unavailable");

        Action act = () => appointment.Complete();

        act.Should().Throw<InvalidOperationException>();
        appointment.Status.Should().Be(AppointmentStatus.Cancelled);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Transition_WithNonUtcTimestamp_ShouldLeaveStateUnchanged(bool complete)
    {
        var appointment = CreateAppointment("Original notes");
        appointment.PopDomainEvents();
        var timestamp = DateTime.SpecifyKind(StartUtc, DateTimeKind.Unspecified);

        Action act = () =>
        {
            if (complete)
                appointment.Complete("New notes", timestamp);
            else
                appointment.Cancel("Unavailable", timestamp);
        };

        act.Should().Throw<ArgumentException>();
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.CompletedUtc.Should().BeNull();
        appointment.CancelledUtc.Should().BeNull();
        appointment.CancellationReason.Should().BeNull();
        appointment.Notes.Should().Be("Original notes");
        appointment.PopDomainEvents().Should().BeEmpty();
    }

    [Fact]
    public void NotesLimit_ShouldApplyToCreateUpdateAndComplete()
    {
        var notes = new string('n', Appointment.NotesMaxLength);
        var appointment = CreateAppointment(notes);
        appointment.PopDomainEvents();

        Action create = () => CreateAppointment(notes + "n");
        Action update = () => appointment.UpdateNotes(notes + "n");
        Action complete = () => appointment.Complete(notes + "n");

        create.Should().Throw<ArgumentException>();
        update.Should().Throw<ArgumentException>();
        complete.Should().Throw<ArgumentException>();
        appointment.Notes.Should().Be(notes);
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
        appointment.PopDomainEvents().Should().BeEmpty();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_WithEmptyReason_ShouldThrow(string? reason)
    {
        var appointment = CreateAppointment();

        Action act = () => appointment.Cancel(reason!);

        act.Should().Throw<ArgumentException>();
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Fact]
    public void Cancel_ShouldEnforceReasonLength()
    {
        var appointment = CreateAppointment();
        var reason = new string('r', Appointment.CancellationReasonMaxLength);

        Action act = () => appointment.Cancel(reason + "r");
        act.Should().Throw<ArgumentException>();
        appointment.Status.Should().Be(AppointmentStatus.Scheduled);

        appointment.Cancel(reason);
        appointment.CancellationReason.Should().Be(reason);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateNotes_WithBlankNotes_ShouldClearNotes(string? notes)
    {
        var appointment = CreateAppointment("Original notes");

        appointment.UpdateNotes(notes);

        appointment.Notes.Should().BeNull();
    }
}
