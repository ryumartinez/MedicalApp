using MedicalApp.Common.Domain.Base;

namespace MedicalApp.Common.Domain.Appointments;

// Ensure strongly typed IDs are registered in 'VogenEfCoreConverters'
// For strongly typed IDs, check out the rule: https://www.ssw.com.au/rules/do-you-use-strongly-typed-ids/
[ValueObject<Guid>]
public readonly partial struct DoctorId;

public class Doctor : Entity<DoctorId>
{
    public const int FullNameMaxLength = 100;
    public const int SpecialtyMaxLength = 50;

    public string FullName
    {
        get;
        set
        {
            ThrowIfNullOrWhiteSpace(value, nameof(FullName));
            ThrowIfGreaterThan(value.Length, FullNameMaxLength, nameof(FullName));
            field = value.Trim();
        }
    } = null!;

    public string Specialty
    {
        get;
        set
        {
            ThrowIfNullOrWhiteSpace(value, nameof(Specialty));
            ThrowIfGreaterThan(value.Length, SpecialtyMaxLength, nameof(Specialty));
            field = value.Trim();
        }
    } = null!;

    private Doctor()
    {
    } // Needed for EF Core

    public static Doctor Create(string fullName, string specialty)
    {
        var doctor = new Doctor
        {
            Id = DoctorId.From(Guid.CreateVersion7()),
            FullName = fullName,
            Specialty = specialty
        };

        return doctor;
    }

    public void UpdateFullName(string fullName)
    {
        FullName = fullName;
    }

    public void UpdateSpecialty(string specialty)
    {
        Specialty = specialty;
    }
}
