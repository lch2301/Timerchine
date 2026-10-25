namespace IntervalTimer;

internal static class MultiTimerApp
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MultiTimerForm());
    }
}
