namespace DeepZoomRipperLibrary
{
    public class DeepZoomRipperOptions
    {
        public int RequestMaxRetryCount { get; set; } = 3;

        public int RequestRetryInterval { get; set; } = 1000;
        public int OutputTileSize { get; set; } = 512;

        public static DeepZoomRipperOptions Default { get; } = new DeepZoomRipperOptions();
    }
}
