namespace MedicalApp.Common.Domain.ApplicationIdentities;

public sealed class ApplicationIdentitySpec : SingleResultSpecification<ApplicationIdentity>
{
    public static ApplicationIdentitySpec ByIssuerAndSubject(string issuer, string subject)
    {
        var spec = new ApplicationIdentitySpec();
        spec.Query.Where(identity => identity.Issuer == issuer && identity.Subject == subject);
        return spec;
    }
}
