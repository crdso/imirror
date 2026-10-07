namespace iMirror.Core.Features;

// Phase 1 exposes availability only. Connection contracts will be added with real backends.
public interface IFeatureService
{
    FeatureStatus Status { get; }
    void ReportAvailability();
}
