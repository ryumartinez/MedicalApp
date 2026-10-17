using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Features.Appointments.GetAppointments;

public record GetAppointmentsResponse(
    IReadOnlyList<GetAppointmentsResponse.AppointmentDto> Appointments, int TotalCount, int Page, int PageSize)
{
    public record AppointmentDto(Guid Id, Guid PatientId, Guid DoctorId, DateTime StartUtc, DateTime EndUtc,
        AppointmentStatus Status, string? Notes);
}
