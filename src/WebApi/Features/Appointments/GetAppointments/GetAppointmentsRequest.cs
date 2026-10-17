using MedicalApp.Common.Domain.Appointments;

namespace MedicalApp.Features.Appointments.GetAppointments;

public class GetAppointmentsRequest
{
    public Guid? PatientId { get; set; }
    public Guid? DoctorId { get; set; }
    public AppointmentStatus? Status { get; set; }
    public DateTimeOffset? FromUtc { get; set; }
    public DateTimeOffset? ToUtc { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
}
