using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;

namespace FFmpeg.AutoGen.Samples;

internal static class FFmpegExt
{
    /// <summary>
    /// replacement for av_opt_set_list:
    /// uses FFmpeg style "term value in list" way
    /// </summary>
    public static unsafe int av_opt_set_list(void* obj, string name, int[] list, int termVal, int flags)
    {
        if (list.Length == 0 || list.First() == termVal)
            return 0;
        for (int i = 1; i < list.Length; ++i)
        {
            if (list[i] == termVal)
                return av_opt_set_list_count(obj, name, list, flags, i - 1);
        }

        return ffmpeg.EINVAL;
    }

    /// <summary>
    /// replacement for av_opt_set_list
    /// sets list with given number of elements (or -1 for full list)
    /// </summary>
    public static unsafe int av_opt_set_list_count(void* obj, string name, int[] list, int flags, int count = -1)
    {
        if (count < 0)
            count = list.Length;
        //av_opt_set_bin copies the data, so stackalloc is fine
        var stackList = stackalloc int[count];
        for (int i = 0; i < count; ++i)
            stackList[i] = list[i];
        var r = ffmpeg.av_opt_set_bin(obj, name, (byte*)stackList, sizeof(int) * count, flags);
        return r;
    }

    public static int ThrowExceptionIfFFmpegError(this int error)
    {
        if (error < 0) throw new FFmpegException(GetStringForFfmpegErrorCode(error), error);
        return error;
    }

    public static unsafe string GetStringForFfmpegErrorCode(int error)
    {
        var bufferSize = 1024;
        var buffer = stackalloc byte[bufferSize];
        ffmpeg.av_strerror(error, buffer, (ulong)bufferSize);
        var message = Marshal.PtrToStringAnsi((IntPtr)buffer);
        return message ?? "No Error Info";
    }
}