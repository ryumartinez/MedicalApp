namespace MedicalApp.Common.Domain.ProgressEntries;

public sealed class ProgressEntrySpec : SingleResultSpecification<ProgressEntry>
{
    public static ProgressEntrySpec ById(ProgressEntryId id)
    {
        var spec = new ProgressEntrySpec();
        spec.Query.Where(entry => entry.Id == id);
        return spec;
    }
}
