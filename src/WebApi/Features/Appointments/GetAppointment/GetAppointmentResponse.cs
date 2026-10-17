using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Features.Appointments.GetAppointment;

public record GetAppointmentResponse(
    Guid Id, Guid PatientId, Guid DoctorId, DateTime StartUtc, DateTime EndUtc, AppointmentStatus Status,
    string? Notes, DateTime? CompletedUtc, DateTime? CancelledUtc, string? CancellationReason,
    GetAppointmentResponse.PatientDto Patient, GetAppointmentResponse.DoctorDto Doctor)
{
    public record PatientDto(Guid Id, string FullName, string Email, string Phone);
    public record DoctorDto(Guid Id, string FullName, string Specialty);
}
