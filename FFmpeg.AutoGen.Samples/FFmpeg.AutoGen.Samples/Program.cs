// See https://aka.ms/new-console-template for more information

using FFmpeg.AutoGen;
using FFmpeg.AutoGen.Abstractions;
using FFmpeg.AutoGen.Bindings.DynamicallyLoaded;
using FFmpeg.AutoGen.Samples;

//mostly check if we can call
DynamicallyLoadedBindings.LibrariesPath = Directory.GetCurrentDirectory();
DynamicallyLoadedBindings.Initialize();
Console.WriteLine("FFMPEG AvCodec Version={0}",ffmpeg.avcodec_version());


var filter = new FFmpegFilteringVideo();

filter.main("TEstPic.jpg");