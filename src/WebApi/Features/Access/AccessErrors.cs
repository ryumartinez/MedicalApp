namespace MedicalApp.Features.Access;

public static class AccessErrors
{
    public static readonly Error TreatmentNotFound = Error.NotFound("Access.TreatmentNotFound", "Treatment relationship is not found");
    public static readonly Error GrantNotFound = Error.NotFound("Access.GrantNotFound", "Plan grant is not found");
    public static readonly Error OwnerGrant = Error.Validation("Access.OwnerGrant", "The plan's owner does not need a grant");
}
