using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using MedicalApp.Common.Domain.Appointments;
using MedicalApp.Features.Appointments.BookAppointment;
using MedicalApp.Features.Appointments.GetAppointment;
using MedicalApp.Features.Appointments.GetAppointments;
using MedicalApp.IntegrationTests.Common;
using Microsoft.EntityFrameworkCore;

namespace MedicalApp.IntegrationTests.Endpoints.Appointments;

public class AppointmentEndpointTests(TestingDatabaseFixture fixture) : IntegrationTestBase(fixture)
{
    private static readonly DateTime StartUtc = DateTime.UtcNow.Date.AddDays(2).AddHours(12);

    private async Task<(Patient Patient, Doctor Doctor)> CreateParticipantsAsync()
    {
        var patient = Patient.Create("Jane Doe", "jane@example.com", "555-123-4567");
        var doctor = Doctor.Create("Dr. Smith", "Cardiology");
        await AddAsync(patient);
        await AddAsync(doctor);
        return (patient, doctor);
    }

    private async Task<Appointment> CreateAppointmentAsync()
    {
        var (patient, doctor) = await CreateParticipantsAsync();
        var appointment = Appointment.Create(patient.Id, doctor.Id, StartUtc, StartUtc.AddMinutes(30), "Initial notes");
        await AddAsync(appointment);
        return appointment;
    }

    private Task<HttpResponseMessage> BookAsync(PatientId patientId, DoctorId doctorId,
        DateTime? startUtc = null, DateTime? endUtc = null, string? notes = null) =>
        GetAnonymousClient().PostAsJsonAsync("/api/appointments", new BookAppointmentRequest(
            patientId.Value, doctorId.Value, startUtc ?? StartUtc, endUtc ?? StartUtc.AddMinutes(30), notes),
            CancellationToken);

    [Theory]
    [InlineData(10)]
    [InlineData(480)]
    public async Task Book_ShouldCreateAppointmentAndReturnLocation(int durationMinutes)
    {
        var (patient, doctor) = await CreateParticipantsAsync();

        var response = await BookAsync(patient.Id, doctor.Id, StartUtc, StartUtc.AddMinutes(durationMinutes), " Notes ");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var body = await response.Content.ReadFromJsonAsync<BookAppointmentResponse>(CancellationToken);
        body.Should().NotBeNull();
        response.Headers.Location!.ToString().Should().EndWith($"/api/appointments/{body!.Id}");
        var saved = await GetQueryable<Appointment>().SingleAsync(CancellationToken);
        saved.PatientId.Should().Be(patient.Id);
        saved.DoctorId.Should().Be(doctor.Id);
        saved.Notes.Should().Be("Notes");
        saved.Status.Should().Be(AppointmentStatus.Scheduled);
    }

    [Theory]
    [InlineData(-10, 10)]
    [InlineData(20, 40)]
    [InlineData(0, 30)]
    [InlineData(10, 20)]
    [InlineData(-10, 40)]
    public async Task Book_WithOverlappingAppointment_ShouldReturnConflict(int startMinutes, int endMinutes)
    {
        var appointment = await CreateAppointmentAsync();

        var response = await BookAsync(appointment.PatientId, appointment.DoctorId,
            StartUtc.AddMinutes(startMinutes), StartUtc.AddMinutes(endMinutes));

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await GetQueryable<Appointment>().CountAsync(CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(-30, 0)]
    [InlineData(30, 60)]
    public async Task Book_WithAdjacentAppointment_ShouldSucceed(int startMinutes, int endMinutes)
    {
        var appointment = await CreateAppointmentAsync();

        var response = await BookAsync(appointment.PatientId, appointment.DoctorId,
            StartUtc.AddMinutes(startMinutes), StartUtc.AddMinutes(endMinutes));

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Book_ShouldIgnoreCancelledAppointmentsAndBlockCompletedAppointments(bool cancelled)
    {
        var appointment = await CreateAppointmentAsync();
        if (cancelled)
            appointment.Cancel("Unavailable");
        else
            appointment.Complete();
        await SaveAsync();

        var response = await BookAsync(appointment.PatientId, appointment.DoctorId);

        response.StatusCode.Should().Be(cancelled ? HttpStatusCode.Created : HttpStatusCode.Conflict);
    }

    [Fact]
    public async Task Book_WithDifferentDoctor_ShouldAllowSameTime()
    {
        var appointment = await CreateAppointmentAsync();
        var doctor = Doctor.Create("Dr. Jones", "General medicine");
        await AddAsync(doctor);

        var response = await BookAsync(appointment.PatientId, doctor.Id);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Book_WithConcurrentRequests_ShouldCreateOnlyOneAppointment()
    {
        var (patient, doctor) = await CreateParticipantsAsync();

        var responses = await Task.WhenAll(Enumerable.Range(0, 5)
            .Select(_ => BookAsync(patient.Id, doctor.Id)));

        responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(4);
        (await GetQueryable<Appointment>().CountAsync(CancellationToken)).Should().Be(1);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Book_WithMissingParticipant_ShouldReturnNotFound(bool missingPatient)
    {
        var (patient, doctor) = await CreateParticipantsAsync();

        var response = await BookAsync(missingPatient ? PatientId.From(Guid.CreateVersion7()) : patient.Id,
            missingPatient ? doctor.Id : DoctorId.From(Guid.CreateVersion7()));

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await GetQueryable<Appointment>().CountAsync(CancellationToken)).Should().Be(0);
    }

    [Theory]
    [InlineData(9)]
    [InlineData(481)]
    [InlineData(0)]
    [InlineData(-10)]
    public async Task Book_WithInvalidDuration_ShouldReturnBadRequest(int durationMinutes)
    {
        var (patient, doctor) = await CreateParticipantsAsync();

        var response = await BookAsync(patient.Id, doctor.Id, StartUtc, StartUtc.AddMinutes(durationMinutes));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await GetQueryable<Appointment>().CountAsync(CancellationToken)).Should().Be(0);
    }

    [Fact]
    public async Task Book_WithoutAdvanceNotice_ShouldReturnBadRequest()
    {
        var (patient, doctor) = await CreateParticipantsAsync();
        var start = DateTime.UtcNow.AddMinutes(14);

        var response = await BookAsync(patient.Id, doctor.Id, start, start.AddMinutes(30));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Book_WithUnspecifiedTimeOrLongNotes_ShouldReturnBadRequest()
    {
        var (patient, doctor) = await CreateParticipantsAsync();
        var unspecified = DateTime.SpecifyKind(StartUtc, DateTimeKind.Unspecified);

        var timeResponse = await BookAsync(patient.Id, doctor.Id, unspecified, unspecified.AddMinutes(30));
        var notesResponse = await BookAsync(patient.Id, doctor.Id, notes: new string('n', Appointment.NotesMaxLength + 1));

        timeResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        notesResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetById_ShouldReturnPatientAndDoctorDetails()
    {
        var appointment = await CreateAppointmentAsync();

        var response = await GetAnonymousClient().GetFromJsonAsync<GetAppointmentResponse>(
            $"/api/appointments/{appointment.Id.Value}", CancellationToken);

        response!.Id.Should().Be(appointment.Id.Value);
        response.Patient.Email.Should().Be("jane@example.com");
        response.Doctor.Specialty.Should().Be("Cardiology");
        response.StartUtc.Kind.Should().Be(DateTimeKind.Utc);
    }

    [Fact]
    public async Task GetAppointments_ShouldCombineAllFiltersAndPaginate()
    {
        var (patient, doctor) = await CreateParticipantsAsync();
        var appointments = Enumerable.Range(0, 5)
            .Select(i => Appointment.Create(patient.Id, doctor.Id, StartUtc.AddHours(i), StartUtc.AddHours(i).AddMinutes(30)))
            .ToList();
        appointments[2].Cancel("Unavailable");
        await AddRangeAsync(appointments);
        var otherPatient = Patient.Create("John Doe", "john@example.com", "555-123-4567");
        await AddAsync(otherPatient);
        await AddAsync(Appointment.Create(otherPatient.Id, doctor.Id, StartUtc.AddMinutes(30), StartUtc.AddMinutes(60)));
        var otherDoctor = Doctor.Create("Dr. Jones", "General medicine");
        await AddAsync(otherDoctor);
        await AddAsync(Appointment.Create(patient.Id, otherDoctor.Id, StartUtc.AddHours(1), StartUtc.AddHours(1.5)));
        var from = Uri.EscapeDataString(StartUtc.ToString("O", CultureInfo.InvariantCulture));
        var to = Uri.EscapeDataString(StartUtc.AddHours(4).ToString("O", CultureInfo.InvariantCulture));
        var url = $"/api/appointments?patientId={patient.Id.Value}&doctorId={doctor.Id.Value}" +
            $"&status=Scheduled&fromUtc={from}&toUtc={to}&page=2&pageSize=2";

        var response = await GetAnonymousClient().GetFromJsonAsync<GetAppointmentsResponse>(url, CancellationToken);

        response!.TotalCount.Should().Be(3);
        response.Page.Should().Be(2);
        response.PageSize.Should().Be(2);
        response.Appointments.Should().ContainSingle().Which.Id.Should().Be(appointments[3].Id.Value);
    }

    [Fact]
    public async Task GetAppointments_WithNoMatches_ShouldReturnEmptyPage()
    {
        var response = await GetAnonymousClient().GetFromJsonAsync<GetAppointmentsResponse>(
            "/api/appointments", CancellationToken);

        response!.TotalCount.Should().Be(0);
        response.Page.Should().Be(1);
        response.PageSize.Should().Be(20);
        response.Appointments.Should().BeEmpty();
    }

    [Theory]
    [InlineData("pageSize=101")]
    [InlineData("pageSize=0")]
    [InlineData("page=0")]
    [InlineData("page=2147483647&pageSize=100")]
    [InlineData("status=99")]
    [InlineData("patientId=00000000-0000-0000-0000-000000000000")]
    [InlineData("fromUtc=not-a-date")]
    [InlineData("fromUtc=2026-10-21T00:00:00Z&toUtc=2026-10-20T00:00:00Z")]
    public async Task GetAppointments_WithInvalidFilters_ShouldReturnBadRequest(string query)
    {
        var response = await GetAnonymousClient().GetAsync($"/api/appointments?{query}", CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    public async Task Transition_ShouldBeIdempotent(string action)
    {
        var appointment = await CreateAppointmentAsync();
        var client = GetAnonymousClient();
        var url = $"/api/appointments/{appointment.Id.Value}/{action}";

        var first = await client.PostAsJsonAsync(url, new { notes = "Completed", reason = "Unavailable" }, CancellationToken);
        var original = await GetQueryable<Appointment>().SingleAsync(CancellationToken);
        var second = await client.PostAsJsonAsync(url, new { notes = "Other notes", reason = "Another reason" }, CancellationToken);
        var repeated = await GetQueryable<Appointment>().SingleAsync(CancellationToken);

        first.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        repeated.CompletedUtc.Should().Be(original.CompletedUtc);
        repeated.CancelledUtc.Should().Be(original.CancelledUtc);
        repeated.Notes.Should().Be(original.Notes);
        repeated.CancellationReason.Should().Be(original.CancellationReason);
        repeated.RowVersion.Should().Equal(original.RowVersion);
        repeated.Status.Should().Be(action == "complete" ? AppointmentStatus.Completed : AppointmentStatus.Cancelled);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    public async Task Transition_FromOppositeTerminalState_ShouldReturnConflict(string action)
    {
        var appointment = await CreateAppointmentAsync();
        if (action == "complete")
            appointment.Cancel("Unavailable");
        else
            appointment.Complete();
        await SaveAsync();

        var response = await GetAnonymousClient().PostAsJsonAsync(
            $"/api/appointments/{appointment.Id.Value}/{action}", new { reason = "Unavailable" }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Cancel_WithoutReason_ShouldReturnBadRequest(string? reason)
    {
        var appointment = await CreateAppointmentAsync();

        var response = await GetAnonymousClient().PostAsJsonAsync(
            $"/api/appointments/{appointment.Id.Value}/cancel", new { reason }, CancellationToken);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Theory]
    [InlineData("complete")]
    [InlineData("cancel")]
    public async Task Transition_WithConcurrentSameAction_ShouldBeIdempotent(string action)
    {
        var appointment = await CreateAppointmentAsync();
        var client = GetAnonymousClient();
        var url = $"/api/appointments/{appointment.Id.Value}/{action}";

        var responses = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => client.PostAsJsonAsync(url, new { notes = "Complete", reason = "Unavailable" }, CancellationToken)));

        responses.Should().OnlyContain(r => r.StatusCode == HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task CompleteAndCancel_Concurrently_ShouldAllowOnlyOneTerminalState()
    {
        var appointment = await CreateAppointmentAsync();
        var client = GetAnonymousClient();

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync($"/api/appointments/{appointment.Id.Value}/complete", new { }, CancellationToken),
            client.PostAsJsonAsync($"/api/appointments/{appointment.Id.Value}/cancel", new { reason = "Unavailable" }, CancellationToken));

        responses.Count(r => r.StatusCode == HttpStatusCode.NoContent).Should().Be(1);
        responses.Count(r => r.StatusCode == HttpStatusCode.Conflict).Should().Be(1);
    }

    [Fact]
    public async Task MissingAppointment_ShouldReturnNotFoundForGetAndTransitions()
    {
        var client = GetAnonymousClient();
        var url = $"/api/appointments/{Guid.CreateVersion7()}";

        var get = await client.GetAsync(url, CancellationToken);
        var complete = await client.PostAsJsonAsync(url + "/complete", new { }, CancellationToken);
        var cancel = await client.PostAsJsonAsync(url + "/cancel", new { reason = "Unavailable" }, CancellationToken);

        get.StatusCode.Should().Be(HttpStatusCode.NotFound);
        complete.StatusCode.Should().Be(HttpStatusCode.NotFound);
        cancel.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }
}
