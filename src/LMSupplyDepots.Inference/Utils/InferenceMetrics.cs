namespace LMSupplyDepots.Inference.Utils;

/// <summary>
/// Utility for collecting and reporting inference metrics
/// </summary>
public class InferenceMetrics
{
    private readonly Stopwatch _stopwatch = new();
    private readonly Dictionary<string, long> _checkpoints = new();
    private long _lastCheckpoint = 0;

    /// <summary>
    /// Starts timing an inference operation
    /// </summary>
    public void Start()
    {
        _stopwatch.Reset();
        _stopwatch.Start();
        _lastCheckpoint = 0;
        _checkpoints.Clear();
    }

    /// <summary>
    /// Records a checkpoint in the inference process
    /// </summary>
    public void RecordCheckpoint(string checkpointName)
    {
        var elapsed = _stopwatch.ElapsedMilliseconds;
        _checkpoints[checkpointName] = elapsed - _lastCheckpoint;
        _lastCheckpoint = elapsed;
    }

    /// <summary>
    /// Stops timing an inference operation
    /// </summary>
    public TimeSpan Stop()
    {
        _stopwatch.Stop();
        return _stopwatch.Elapsed;
    }

    /// <summary>
    /// Gets the current elapsed time
    /// </summary>
    public TimeSpan GetElapsed()
    {
        return _stopwatch.Elapsed;
    }

    /// <summary>
    /// Gets all recorded checkpoints
    /// </summary>
    public IReadOnlyDictionary<string, long> GetCheckpoints()
    {
        return _checkpoints;
    }

    /// <summary>
    /// Gets the time between two checkpoints
    /// </summary>
    public long GetTimeBetweenCheckpoints(string startCheckpoint, string endCheckpoint)
    {
        if (!_checkpoints.TryGetValue(startCheckpoint, out var startTime) ||
            !_checkpoints.TryGetValue(endCheckpoint, out var endTime))
        {
            return -1;
        }

        return endTime - startTime;
    }

    /// <summary>
    /// Formats the metrics report as a string
    /// </summary>
    public string FormatReport()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Total time: {_stopwatch.ElapsedMilliseconds}ms");

        if (_checkpoints.Count > 0)
        {
            sb.AppendLine("Checkpoints:");
            foreach (var checkpoint in _checkpoints)
            {
                sb.AppendLine($"  {checkpoint.Key}: {checkpoint.Value}ms");
            }
        }

        return sb.ToString();
    }
}