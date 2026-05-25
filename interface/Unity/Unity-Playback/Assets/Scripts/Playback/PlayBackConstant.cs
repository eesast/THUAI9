namespace THUAI9.Unity.Playback
{
    /// <summary>
    /// THUAI9 回放相关常量。
    /// </summary>
    public static class PlayBackConstant
    {
        public const int FRAME_PER_SECOND = 60;
        public const float MILLISECONDS_PER_FRAME = 1000f / FRAME_PER_SECOND;
        public const float SERVER_FRAME_INTERVAL_MS = 50f;
        public const float MAX_REASONABLE_FRAME_DELTA_MS = 1000f;
        public const float DEFAULT_PLAY_SPEED = 1.0f;
        public const float MIN_PLAY_SPEED = 0.25f;
        public const float MAX_PLAY_SPEED = 4.0f;
        public const float SPEED_STEP = 0.25f;
        public const string MAGIC_NUMBER = "PB";
        public const int FILE_VERSION = 9;
        public const int LEGACY_FILE_VERSION = 7;
        public const int MAX_MESSAGE_COUNT = 100000;
        public const int MAX_DECOMPRESSED_PLAYBACK_BYTES = 256 * 1024 * 1024;
        public const string DEFAULT_PLAYBACK_PATH = "Playback/";
        public const string PLAYBACK_EXTENSION = ".thuaipb";

        public static bool IsSupportedFileVersion(int version)
        {
            return version == FILE_VERSION || version == LEGACY_FILE_VERSION;
        }
    }
}
