namespace NetworkMonitor;

internal sealed class RateHistory
{
    private readonly double[] _down;
    private readonly double[] _up;

    public RateHistory(int length)
    {
        _down = new double[length];
        _up = new double[length];
    }

    public int Length => _down.Length;

    public void Push(double down, double up)
    {
        Array.Copy(_down, 1, _down, 0, _down.Length - 1);
        Array.Copy(_up, 1, _up, 0, _up.Length - 1);
        _down[^1] = down;
        _up[^1] = up;
    }

    public double[] CopyDown() => (double[])_down.Clone();
    public double[] CopyUp() => (double[])_up.Clone();
}
