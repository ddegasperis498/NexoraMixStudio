using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace NexoraMix.Audio.Playback;

internal static class StereoSampleProviderFactory
{
    public static ISampleProvider Create(ISampleProvider source, int targetSampleRate)
    {
        ISampleProvider stereo = source.WaveFormat.Channels switch
        {
            1 => new MonoToStereoSampleProvider(source),
            2 => source,
            _ => new StereoDownmixSampleProvider(source)
        };

        return stereo.WaveFormat.SampleRate == targetSampleRate
            ? stereo
            : new WdlResamplingSampleProvider(stereo, targetSampleRate);
    }

    private sealed class StereoDownmixSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private float[] _sourceBuffer = Array.Empty<float>();

        public StereoDownmixSampleProvider(ISampleProvider source)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, 2);
        }

        public WaveFormat WaveFormat { get; }

        public int Read(float[] buffer, int offset, int count)
        {
            var outputFrames = count / 2;
            var inputChannels = _source.WaveFormat.Channels;
            var required = outputFrames * inputChannels;
            if (_sourceBuffer.Length < required) _sourceBuffer = new float[required];

            var sourceRead = _source.Read(_sourceBuffer, 0, required);
            var framesRead = sourceRead / inputChannels;

            for (var frame = 0; frame < framesRead; frame++)
            {
                var sourceOffset = frame * inputChannels;
                var left = _sourceBuffer[sourceOffset];
                var right = inputChannels > 1 ? _sourceBuffer[sourceOffset + 1] : left;

                if (inputChannels > 2)
                {
                    double extra = 0;
                    for (var channel = 2; channel < inputChannels; channel++)
                        extra += _sourceBuffer[sourceOffset + channel];
                    var contribution = (float)(extra / Math.Max(1, inputChannels - 2) * 0.25d);
                    left += contribution;
                    right += contribution;
                }

                buffer[offset + frame * 2] = left;
                buffer[offset + frame * 2 + 1] = right;
            }

            return framesRead * 2;
        }
    }
}
