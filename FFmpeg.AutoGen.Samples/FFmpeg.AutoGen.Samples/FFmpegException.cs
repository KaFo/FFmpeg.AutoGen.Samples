namespace FFmpeg.AutoGen.Samples;

public class FFmpegException : Exception
{
    public readonly int ErrorCode;

    public FFmpegException()
    {
    }

    public FFmpegException(string message, int errorCode = 0)
        : base(message)
    {
        this.ErrorCode = errorCode;
    }

    public FFmpegException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}