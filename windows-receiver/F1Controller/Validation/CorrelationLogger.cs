using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace F1Controller.Validation;

/// <summary>
/// Logs sent-vs-received steering/throttle/brake pairs and computes
/// Pearson R² correlation and mean absolute error in real time.
/// </summary>
public sealed class CorrelationLogger : IDisposable
{
    private readonly StreamWriter _csv;
    private readonly Stopwatch _sw = Stopwatch.StartNew();
    private readonly object _lock = new();

    // Rolling window for correlation (last N pairs)
    private const int WindowSize = 500;
    private readonly Queue<(double sentSteer, double gameSteer,
                             double sentThrottle, double gameThrottle,
                             double sentBrake, double gameBrake)> _window = new();

    // Running stats
    public double SteerR2 { get; private set; }
    public double SteerMAE { get; private set; }
    public double ThrottleR2 { get; private set; }
    public double BrakeR2 { get; private set; }
    public int SampleCount { get; private set; }

    public CorrelationLogger()
    {
        var path = $"correlation_log_{DateTime.Now:yyyyMMdd_HHmmss}.csv";
        _csv = new StreamWriter(path, append: false);
        _csv.WriteLine("timestamp,sent_steer_i16,game_steer_float,sent_throttle,game_throttle,sent_brake,game_brake");
        Console.WriteLine($"[Validation] Correlation log: {path}");
    }

    /// <summary>
    /// Record a matched pair of sent input and game-observed telemetry.
    /// </summary>
    /// <param name="sentSteer">Steering value sent to ViGEm (i16, -32768..32767).</param>
    /// <param name="gameSteer">Steering value read from F1 telemetry (float, -1..1).</param>
    /// <param name="sentThrottle">Throttle sent (u8, 0..255).</param>
    /// <param name="gameThrottle">Throttle from telemetry (float, 0..1).</param>
    /// <param name="sentBrake">Brake sent (u8, 0..255).</param>
    /// <param name="gameBrake">Brake from telemetry (float, 0..1).</param>
    public void Record(short sentSteer, float gameSteer,
                       byte sentThrottle, float gameThrottle,
                       byte sentBrake, float gameBrake)
    {
        lock (_lock)
        {
            _csv.WriteLine($"{DateTime.UtcNow:O},{sentSteer},{gameSteer:F6},{sentThrottle},{gameThrottle:F6},{sentBrake},{gameBrake:F6}");

            // Normalize sent values to match game ranges for correlation
            // Steer: i16 → -1..1
            double normSteer = sentSteer / 32768.0;
            // Throttle: u8 → 0..1
            double normThrottle = sentThrottle / 255.0;
            // Brake: u8 → 0..1
            double normBrake = sentBrake / 255.0;

            _window.Enqueue((normSteer, gameSteer, normThrottle, gameThrottle, normBrake, gameBrake));
            if (_window.Count > WindowSize)
                _window.Dequeue();

            SampleCount++;

            // Recompute stats every 50 samples to avoid per-packet overhead
            if (SampleCount % 50 == 0 && _window.Count >= 10)
            {
                var items = _window.ToArray();
                SteerR2 = PearsonR2(items.Select(x => x.sentSteer).ToArray(),
                                     items.Select(x => x.gameSteer).ToArray());
                SteerMAE = MeanAbsoluteError(items.Select(x => x.sentSteer).ToArray(),
                                              items.Select(x => x.gameSteer).ToArray());
                ThrottleR2 = PearsonR2(items.Select(x => x.sentThrottle).ToArray(),
                                        items.Select(x => x.gameThrottle).ToArray());
                BrakeR2 = PearsonR2(items.Select(x => x.sentBrake).ToArray(),
                                     items.Select(x => x.gameBrake).ToArray());

                _csv.Flush();
            }
        }
    }

    /// <summary>
    /// Compute Pearson correlation coefficient squared (R²).
    /// Returns 0 if insufficient variance.
    /// </summary>
    private static double PearsonR2(double[] x, double[] y)
    {
        int n = x.Length;
        if (n < 2) return 0;

        double sumX = 0, sumY = 0, sumXY = 0, sumX2 = 0, sumY2 = 0;
        for (int i = 0; i < n; i++)
        {
            sumX += x[i];
            sumY += y[i];
            sumXY += x[i] * y[i];
            sumX2 += x[i] * x[i];
            sumY2 += y[i] * y[i];
        }

        double denominator = (n * sumX2 - sumX * sumX) * (n * sumY2 - sumY * sumY);
        if (denominator <= 0) return 0;

        double r = (n * sumXY - sumX * sumY) / Math.Sqrt(denominator);
        return r * r; // R²
    }

    private static double MeanAbsoluteError(double[] x, double[] y)
    {
        double sum = 0;
        for (int i = 0; i < x.Length; i++)
            sum += Math.Abs(x[i] - y[i]);
        return x.Length > 0 ? sum / x.Length : 0;
    }

    public void PrintStats()
    {
        lock (_lock)
        {
            if (SampleCount < 10) return;
            Console.WriteLine($"[Correlation] Samples={SampleCount}  Steer R²={SteerR2:F4} MAE={SteerMAE:F4}  " +
                              $"Throttle R²={ThrottleR2:F4}  Brake R²={BrakeR2:F4}");
        }
    }

    public void Dispose()
    {
        _csv.Flush();
        _csv.Dispose();
    }
}
