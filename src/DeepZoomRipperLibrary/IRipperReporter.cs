namespace DeepZoomRipperLibrary
{
    public interface IRipperInitialLayerAcquisitionReporter
    {
        void ReportStartInitialLayerAcquisition(int tileCount);

        void ReportInitialLayerAcquisitionProgress(int completedTileCount, int totalTileCount);

        void ReportCompleteInitialLayerAcquisition(int tileCount, long fileSize);
    }

    public interface IRipperReducedResolutionGenerationReporter
    {
        void ReportStartReducedResolutionGeneration(int layers);

        void ReportCompleteReducedResolutionGeneration(int layers);

        void ReportStartReducedResolutionLayerGeneration(int layer, int tileCount, int width, int height);

        void ReportReducedResolutionLayerGenerationProgress(int layer, int completedTileCount, int totalTileCount);

        void ReportCompleteReducedResolutionLayerGeneration(int layer, int tileCount, long fileSize);
    }

}
