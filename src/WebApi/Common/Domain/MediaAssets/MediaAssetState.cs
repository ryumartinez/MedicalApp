namespace MedicalApp.Common.Domain.MediaAssets;

public enum MediaAssetState
{
    /// <summary>Upload URL issued. Not usable in answers.</summary>
    Pending = 0,

    /// <summary>Validated and promoted to its final key. Can be referenced by a draft answer.</summary>
    Ready = 1,

    /// <summary>Referenced by a submitted entry. Only submission sets this state.</summary>
    Attached = 2,

    /// <summary>Abandoned. Its objects are removed by cleanup.</summary>
    Expired = 3
}
