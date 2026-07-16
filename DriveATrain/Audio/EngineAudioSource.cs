namespace DriveATrain.Audio;

public class EngineAudioSource
{
    private readonly float[] _samples; // mono, -1..1, looping recording
    private readonly int _sourceRate;
    private double _pos;
    public double Pitch = 1.0; // 1.0 = recorded pitch; >1 higher/faster, <1 lower/slower

    public EngineAudioSource(float[] samples, int sourceRate)
    {
        _samples = samples;
        _sourceRate = sourceRate;
    }

    public void Render(short[] outBuffer, int count, int outputRate)
    {
        double step = (double)_sourceRate / outputRate * Pitch;
        for (int i = 0; i < count; i++)
        {
            int i0 = (int)_pos % _samples.Length;
            int i1 = (i0 + 1) % _samples.Length;
            double frac = _pos - Math.Floor(_pos);
            float s = (float)(_samples[i0] * (1 - frac) + _samples[i1] * frac);
            outBuffer[i] = (short)(Math.Clamp(s, -1f, 1f) * short.MaxValue);
            _pos += step;
            if (_pos >= _samples.Length) _pos -= _samples.Length;
        }
    }
}
