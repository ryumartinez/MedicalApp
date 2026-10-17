using System.Text.RegularExpressions;
using MedicalApp.Common.Domain.Base;

namespace MedicalApp.Common.Domain.Appointments;

[ValueObject<Guid>]
public readonly partial struct PatientId;

public class Patient : Entity<PatientId>
{
    public const int FullNameMaxLength = 100;
    public const int EmailMaxLength = 254;
    public const int PhoneMaxLength = 17;

    public string FullName
    {
        get;
        private set
        {
            ThrowIfNullOrWhiteSpace(value, nameof(FullName));
            ThrowIfGreaterThan(value.Length, FullNameMaxLength, nameof(FullName));
            field = value.Trim();
        }
    } = null!;

    public string Email
    {
        get;
        private set
        {
            ThrowIfNullOrWhiteSpace(value, nameof(Email));
            ThrowIfGreaterThan(value.Length, EmailMaxLength, nameof(Email));
            if (!Regex.IsMatch(value, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                throw new ArgumentException("Invalid email format", nameof(Email));

            field = value.Trim().ToLowerInvariant();
        }
    } = null!;

    public string Phone
    {
        get;
        private set
        {
            ThrowIfNullOrWhiteSpace(value, nameof(Phone));
            var phone = value.Trim();
            ThrowIfGreaterThan(phone.Length, PhoneMaxLength, nameof(Phone));
            if (!Regex.IsMatch(phone, @"^[\+]?[1-9]?[\d\s\-\(\)\.]{10,15}$"))
                throw new ArgumentException("Invalid phone format", nameof(Phone));

            field = phone;
        }
    } = null!;

    private Patient()
    {
    }

    public static Patient Create(string fullName, string email, string phone) => new()
    {
        Id = PatientId.From(Guid.CreateVersion7()),
        FullName = fullName,
        Email = email,
        Phone = phone
    };

    public void UpdateFullName(string fullName) => FullName = fullName;
    public void UpdateEmail(string email) => Email = email;
    public void UpdatePhone(string phone) => Phone = phone;
}
