using MedicalApp.Common.Domain.Doctors;

namespace MedicalApp.Common.Domain.ProgressForms;

public sealed class ProgressFormSpec : SingleResultSpecification<ProgressForm>
{
    /// <summary>
    /// The doctor's own form. Other doctors' forms are indistinguishable from missing ones.
    /// </summary>
    public static ProgressFormSpec OwnedById(ProgressFormId id, DoctorId doctorId)
    {
        var spec = new ProgressFormSpec();
        spec.Query.Where(form => form.Id == id && form.DoctorId == doctorId);
        return spec;
    }
}

public sealed class ProgressFormListSpec : Specification<ProgressForm>
{
    public static ProgressFormListSpec Owned(DoctorId doctorId, string? search)
    {
        var spec = new ProgressFormListSpec();
        spec.Query.Where(form => form.DoctorId == doctorId);
        if (!string.IsNullOrWhiteSpace(search))
            spec.Query.Where(form => EF.Functions.ILike(form.Title, "%" + search.Trim() + "%"));
        return spec;
    }

    public static ProgressFormListSpec OwnedPage(DoctorId doctorId, string? search, int page, int pageSize)
    {
        var spec = Owned(doctorId, search);
        spec.Query.OrderBy(form => form.Title).ThenBy(form => form.Id).Skip((page - 1) * pageSize).Take(pageSize);
        return spec;
    }
}

public sealed class ProgressFormVersionSpec : Specification<ProgressFormVersion>
{
    public static ProgressFormVersionSpec ForForm(ProgressFormId formId)
    {
        var spec = new ProgressFormVersionSpec();
        spec.Query.Where(version => version.FormId == formId).OrderBy(version => version.VersionNumber);
        return spec;
    }

    public static ProgressFormVersionSpec OwnedById(ProgressFormVersionId id, DoctorId doctorId)
    {
        var spec = new ProgressFormVersionSpec();
        spec.Query.Where(version => version.Id == id && version.DoctorId == doctorId);
        return spec;
    }
}
