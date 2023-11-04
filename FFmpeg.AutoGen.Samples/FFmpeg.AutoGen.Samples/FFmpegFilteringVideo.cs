using System.Runtime.InteropServices;
using FFmpeg.AutoGen.Abstractions;

namespace FFmpeg.AutoGen.Samples;

/// <summary>
/// based on https://www.ffmpeg.org/doxygen/trunk/filtering_video_8c-example.html
/// </summary>
public unsafe class FFmpegFilteringVideo
{
    public const int
        AV_BUFFERSRC_FLAG_KEEP_REF = 8; //https://github.com/FFmpeg/FFmpeg/blob/master/libavfilter/buffersrc.h

    AVFormatContext* _fmt_ctx;
    AVCodecContext* _dec_ctx;
    AVFilterContext* _buffersink_ctx;
    AVFilterContext* _buffersrc_ctx;
    AVFilterGraph* _filter_graph;
    int video_stream_index = -1;
    //long last_pts = ffmpeg.AV_NOPTS_VALUE;

    public int main(string filePath, string filter_descr = "scale=78:24,transpose=cclock")
    {
        int ret;
        AVPacket* packet;
        AVFrame* frame;
        AVFrame* filt_frame;

        if (!System.IO.File.Exists(filePath))
            throw new FileNotFoundException(filePath);

        frame = ffmpeg.av_frame_alloc();
        filt_frame = ffmpeg.av_frame_alloc();
        packet = ffmpeg.av_packet_alloc();
        if (frame == null || filt_frame == null || packet == null)
        {
            throw new InvalidOperationException("Could not allocate frame or packet");
        }

        try
        {
            open_input_file(filePath);
            init_filters(filter_descr);

            /* read all packets */
            while (true)
            {
                if ((ret = ffmpeg.av_read_frame(_fmt_ctx, packet)) < 0)
                    break;

                if (packet->stream_index == video_stream_index)
                {
                    ret = ffmpeg.avcodec_send_packet(_dec_ctx, packet);
                    if (ret < 0)
                    {
                        Console.WriteLine("Error while sending a packet to the decoder");
                        break;
                    }

                    while (ret >= 0)
                    {
                        ret = ffmpeg.avcodec_receive_frame(_dec_ctx, frame);
                        if (ret == ffmpeg.EAGAIN || ret == ffmpeg.AVERROR_EOF)
                        {
                            break;
                        }
                        else if (ret < 0)
                        {
                            Console.WriteLine("Error while receiving a frame from the decoder");
                            return -1;
                        }

                        frame->pts = frame->best_effort_timestamp;

                        /* push the decoded frame into the filtergraph */
                        if (ffmpeg.av_buffersrc_add_frame_flags(_buffersrc_ctx, frame,
                                AV_BUFFERSRC_FLAG_KEEP_REF) < 0)
                        {
                            Console.WriteLine("Error while feeding the filtergraph");
                            break;
                        }

                        /* pull filtered frames from the filtergraph */
                        while (true)
                        {
                            ret = ffmpeg.av_buffersink_get_frame(_buffersink_ctx, filt_frame);
                            if (ret == ffmpeg.EAGAIN || ret == ffmpeg.AVERROR_EOF)
                                break;
                            if (ret < 0)
                                return -1;
                            display_frame(filt_frame, _buffersink_ctx->inputs[0]->time_base);
                            ffmpeg.av_frame_unref(filt_frame);
                        }

                        ffmpeg.av_frame_unref(frame);
                    }
                }

                ffmpeg.av_packet_unref(packet);
            }
        }
        finally
        {
            var filter_graph = _filter_graph;
            ffmpeg.avfilter_graph_free(&filter_graph);
            _filter_graph = null;
            var dec_ctx = _dec_ctx;
            ffmpeg.avcodec_free_context(&dec_ctx);
            _dec_ctx = null;
            var fmt_ctx = _fmt_ctx;
            ffmpeg.avformat_close_input(&fmt_ctx);
            _fmt_ctx = null;
            ffmpeg.av_frame_free(&frame);
            ffmpeg.av_frame_free(&filt_frame);
            ffmpeg.av_packet_free(&packet);
        }

        return 0;
    }

    private int open_input_file(string filename)
    {
        AVCodec* dec;
        int ret;

        var fmt_ctx = _fmt_ctx;
        if ((ret = ffmpeg.avformat_open_input(&fmt_ctx, filename, null, null)) < 0)
        {
            throw new InvalidOperationException("Cannot open input file");
        }

        _fmt_ctx = fmt_ctx;

        if ((ret = ffmpeg.avformat_find_stream_info(_fmt_ctx, null)) < 0)
        {
            throw new InvalidOperationException("Cannot find stream information\n");
        }

        /* select the video stream */
        ret = ffmpeg.av_find_best_stream(_fmt_ctx, AVMediaType.AVMEDIA_TYPE_VIDEO, -1, -1, &dec, 0);
        if (ret < 0)
        {
            throw new InvalidOperationException("Cannot find a video stream in the input file\n");
        }

        video_stream_index = ret;

        /* create decoding context */
        _dec_ctx = ffmpeg.avcodec_alloc_context3(dec);
        if (_dec_ctx == null)
            throw new OutOfMemoryException();
        ffmpeg.avcodec_parameters_to_context(_dec_ctx, fmt_ctx->streams[video_stream_index]->codecpar);

        /* init the video decoder */
        if ((ret = ffmpeg.avcodec_open2(_dec_ctx, dec, null)) < 0)
        {
            throw new InvalidOperationException("Cannot open video decoder\n");
        }

        return 0;
    }

    private void init_filters(string filters_descr)
    {
        AVFilter* buffersrc = ffmpeg.avfilter_get_by_name("buffer");
        AVFilter* buffersink = ffmpeg.avfilter_get_by_name("buffersink");
        AVFilterInOut* outputs = ffmpeg.avfilter_inout_alloc();
        AVFilterInOut* inputs = ffmpeg.avfilter_inout_alloc();

        try
        {
            AVRational time_base = _fmt_ctx->streams[video_stream_index]->time_base;
            AVPixelFormat[] pix_fmts = { AVPixelFormat.AV_PIX_FMT_GRAY8, AVPixelFormat.AV_PIX_FMT_NONE };

            _filter_graph = ffmpeg.avfilter_graph_alloc();
            if (outputs == null || inputs == null || _filter_graph == null)
            {
                throw new OutOfMemoryException();
            }

            /* buffer video source: the decoded frames from the decoder will be inserted here. */
            string args = string.Format(
                //"video_size=%dx%d:pix_fmt=%d:time_base=%d/%d:pixel_aspect=%d/%d",
                "video_size={0}x{1}:pix_fmt={2}:time_base={3}/{4}:pixel_aspect={5}/{6}",
                //"video_size={0}x{1}:pix_fmt={2}:time_base={3}/{4}:pixel_aspect={5}/{6}",
                _dec_ctx->width, _dec_ctx->height, (int)_dec_ctx->pix_fmt,
                time_base.num, time_base.den,
                _dec_ctx->sample_aspect_ratio.num, _dec_ctx->sample_aspect_ratio.den);

            var buffersrc_ctx = _buffersrc_ctx;
            ffmpeg.avfilter_graph_create_filter(&buffersrc_ctx, buffersrc, "in",
                args, null, _filter_graph).ThrowExceptionIfFFmpegError();
            _buffersrc_ctx = buffersrc_ctx;

            /* buffer video sink: to terminate the filter chain. */
            var buffersink_ctx = _buffersink_ctx;
            ffmpeg.avfilter_graph_create_filter(&buffersink_ctx, buffersink, "out",
                null, null, _filter_graph).ThrowExceptionIfFFmpegError();
            _buffersink_ctx = buffersink_ctx;

            // #define av_opt_set_int_list    (av_int_list_length(val, term) > INT_MAX / sizeof(*(val)) ? \
            //AVERROR(EINVAL) : \
            //av_opt_set_bin(obj, name, (const uint8_t *)(val), \
            //av_int_list_length(val, term) * sizeof(*(val)), flags))

            //ret = ffmpeg.av_opt_set_int_list(buffersink_ctx, "pix_fmts", pix_fmts,
            //    AV_PIX_FMT_NONE, AV_OPT_SEARCH_CHILDREN);
            FFmpegExt.av_opt_set_list(buffersink_ctx, "pix_fmts", pix_fmts.Cast<int>().ToArray()
                , (int)AVPixelFormat.AV_PIX_FMT_NONE,
                ffmpeg.AV_OPT_SEARCH_CHILDREN).ThrowExceptionIfFFmpegError();

            /*
             * Set the endpoints for the filter graph. The filter_graph will
             * be linked to the graph described by filters_descr.
             */

            /*
             * The buffer source output must be connected to the input pad of
             * the first filter described by filters_descr; since the first
             * filter input label is not specified, it is set to "in" by
             * default.
             */
            outputs->name = (byte*)Marshal.StringToHGlobalAnsi("in").ToPointer(); //TODO: DISPOSE! av_strdup("in");
            outputs->filter_ctx = buffersrc_ctx;
            outputs->pad_idx = 0;
            outputs->next = null;

            /*
             * The buffer sink input must be connected to the output pad of
             * the last filter described by filters_descr; since the last
             * filter output label is not specified, it is set to "out" by
             * default.
             */
            inputs->name = (byte*)Marshal.StringToHGlobalAnsi("out").ToPointer(); //TODO: DISPOSE!
            inputs->filter_ctx = buffersink_ctx;
            inputs->pad_idx = 0;
            inputs->next = null;

            ffmpeg.avfilter_graph_parse_ptr(_filter_graph, filters_descr,
                &inputs, &outputs, null).ThrowExceptionIfFFmpegError();
            ffmpeg.avfilter_graph_config(_filter_graph, null).ThrowExceptionIfFFmpegError();
        }
        finally
        {
            ffmpeg.avfilter_inout_free(&inputs);
            ffmpeg.avfilter_inout_free(&outputs);
        }
    }

    private void display_frame(AVFrame* filtFrame, AVRational avRational)
    {
    }
}