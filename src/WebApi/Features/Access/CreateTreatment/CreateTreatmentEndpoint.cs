using Ardalis.Specification.EntityFrameworkCore;
using EntityFramework.Exceptions.Common;
using MedicalApp.Common.Domain.DoctorPatientRelationships;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.FastEndpoints;

namespace MedicalApp.Features.Access.CreateTreatment;

public class CreateTreatmentEndpoint(ApplicationDbContext dbContext) : Endpoint<CreateTreatmentRequest, CreateTreatmentResponse>
{
    public override void Configure()
    {
        Post("/treatments");
        Group<AccessGroup>();
        Description(x => x.WithName("CreateTreatment").ProducesProblemDetails(400).ProducesProblemDetails(404));
    }

    public override async Task HandleAsync(CreateTreatmentRequest req, CancellationToken ct)
    {
        var doctorId = DoctorId.From(req.DoctorId);
        var patientId = PatientId.From(req.PatientId);
        if (!await dbContext.Doctors.WithSpecification(DoctorSpec.ById(doctorId)).AnyAsync(ct))
        {
            await HttpContext.SendErrorsAsync([DoctorErrors.NotFound], ct);
            return;
        }
        if (!await dbContext.Patients.WithSpecification(PatientSpec.ById(patientId)).AnyAsync(ct))
        {
            await HttpContext.SendErrorsAsync([PatientErrors.NotFound], ct);
            return;
        }

        var relationship = await FindAsync(doctorId, patientId, ct);
        if (relationship is null)
        {
            relationship = DoctorPatientRelationship.Create(doctorId, patientId);
            dbContext.DoctorPatientRelationships.Add(relationship);
            try
            {
                await dbContext.SaveChangesAsync(ct);
            }
            catch (UniqueConstraintException)
            {
                // A concurrent request created it. Continue with the stored relationship.
                dbContext.ChangeTracker.Clear();
                relationship = (await FindAsync(doctorId, patientId, ct))!;
            }
        }

        if (!relationship.IsActive)
        {
            relationship.Reactivate();
            await dbContext.SaveChangesAsync(ct);
        }

        await Send.OkAsync(new CreateTreatmentResponse(relationship.Id.Value, req.DoctorId, req.PatientId, relationship.IsActive), ct);
    }

    private Task<DoctorPatientRelationship?> FindAsync(DoctorId doctorId, PatientId patientId, CancellationToken ct) =>
        dbContext.DoctorPatientRelationships
            .WithSpecification(DoctorPatientRelationshipSpec.ByDoctorAndPatient(doctorId, patientId))
            .SingleOrDefaultAsync(ct);
}
