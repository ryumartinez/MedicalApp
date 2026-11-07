using Ardalis.Specification.EntityFrameworkCore;
using EntityFramework.Exceptions.Common;
using MedicalApp.Common.Domain.ApplicationIdentities;
using MedicalApp.Common.Domain.Doctors;
using MedicalApp.Common.Domain.Patients;
using MedicalApp.Common.FastEndpoints;

namespace MedicalApp.Features.Accounts.LinkAccount;

public class LinkAccountEndpoint(ApplicationDbContext dbContext)
    : Endpoint<LinkAccountRequest, LinkAccountResponse>
{
    public override void Configure()
    {
        Post("/{id}/links");
        Group<AccountsGroup>();
        Description(x => x.WithName("LinkAccount")
            .ProducesProblemDetails(400).ProducesProblemDetails(404).ProducesProblemDetails(409));
    }

    public override async Task HandleAsync(LinkAccountRequest req, CancellationToken ct)
    {
        ErrorOr<ApplicationIdentity> result;
        try
        {
            result = await dbContext.Database.CreateExecutionStrategy().ExecuteAsync(() => LinkAsync(req, ct));
        }
        catch (UniqueConstraintException)
        {
            result = ApplicationIdentityErrors.AlreadyLinked;
        }

        if (result.IsError)
        {
            result.Errors.ForEach(error => AddError(error.Description, error.Code));
            await Send.ErrorsAsync(result.FirstError.ToStatusCode(), ct);
            return;
        }

        await Send.OkAsync(new LinkAccountResponse(result.Value.Id.Value, result.Value.DoctorId?.Value, result.Value.PatientId?.Value), ct);
    }

    private async Task<ErrorOr<ApplicationIdentity>> LinkAsync(LinkAccountRequest req, CancellationToken ct)
    {
        dbContext.ChangeTracker.Clear();
        await using var transaction = await dbContext.Database.BeginTransactionAsync(ct);

        // Lock the mapping so concurrent retries cannot each create a new record.
        var identity = await dbContext.ApplicationIdentities
            .FromSqlInterpolated($"SELECT * FROM \"ApplicationIdentities\" WHERE \"Id\" = {req.Id} FOR UPDATE")
            .WithSpecification(ApplicationIdentitySpec.ById(ApplicationIdentityId.From(req.Id)))
            .SingleOrDefaultAsync(ct);
        if (identity is null)
            return ApplicationIdentityErrors.NotFound;

        var doctorResult = await LinkDoctorAsync(identity, req, ct);
        if (doctorResult.IsError)
            return doctorResult.Errors;
        var patientResult = await LinkPatientAsync(identity, req, ct);
        if (patientResult.IsError)
            return patientResult.Errors;

        await dbContext.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);
        return identity;
    }

    private async Task<ErrorOr<Success>> LinkDoctorAsync(ApplicationIdentity identity, LinkAccountRequest req, CancellationToken ct)
    {
        if (req.DoctorId is null && req.NewDoctor is null)
            return Result.Success;

        if (identity.DoctorId is { } linked)
        {
            // A retry for the same link changes nothing. A request for a new doctor reuses the linked one.
            return req.DoctorId is null || req.DoctorId == linked.Value ? Result.Success : ApplicationIdentityErrors.AlreadyLinked;
        }

        if (identity.Enabled)
            return ApplicationIdentityErrors.MustBeDisabled;

        DoctorId doctorId;
        if (req.DoctorId is { } existingId)
        {
            doctorId = DoctorId.From(existingId);
            if (!await dbContext.Doctors.WithSpecification(DoctorSpec.ById(doctorId)).AnyAsync(ct))
                return DoctorErrors.NotFound;
        }
        else
        {
            var doctor = Doctor.Create(req.NewDoctor!.FullName, req.NewDoctor.Specialty);
            dbContext.Doctors.Add(doctor);
            doctorId = doctor.Id;
        }

        identity.LinkDoctor(doctorId);
        return Result.Success;
    }

    private async Task<ErrorOr<Success>> LinkPatientAsync(ApplicationIdentity identity, LinkAccountRequest req, CancellationToken ct)
    {
        if (req.PatientId is null && req.NewPatient is null)
            return Result.Success;

        if (identity.PatientId is { } linked)
            return req.PatientId is null || req.PatientId == linked.Value ? Result.Success : ApplicationIdentityErrors.AlreadyLinked;

        if (identity.Enabled)
            return ApplicationIdentityErrors.MustBeDisabled;

        PatientId patientId;
        if (req.PatientId is { } existingId)
        {
            patientId = PatientId.From(existingId);
            if (!await dbContext.Patients.WithSpecification(PatientSpec.ById(patientId)).AnyAsync(ct))
                return PatientErrors.NotFound;
        }
        else
        {
            var patient = Patient.Create(req.NewPatient!.FullName, req.NewPatient.Email, req.NewPatient.Phone);
            dbContext.Patients.Add(patient);
            patientId = patient.Id;
        }

        identity.LinkPatient(patientId);
        return Result.Success;
    }
}
