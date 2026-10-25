using System.Drawing;
using System.Reflection;
using System.Text.Json;
using System.Windows.Forms;

namespace IntervalTimer;

internal static class DurationInputChecks
{
    private static string output = "";
    private static readonly List<string> passed = [];
    private static T Field<T>(object target, string name) => (T)target.GetType()
        .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(target)!;
    private static void Call(object target, string name, params object[] args) => target.GetType()
        .GetMethod(name, BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(target, args);
    private static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    private static void Pass(string message) { passed.Add(message); Console.WriteLine("PASS " + message); }
    private static TextBox[] Parts(TimerCard card) => Field<TextBox[]>(card.TimeInput, "parts");
    private static bool Highlighted(TimerCard card) => Parts(card).All(part => part.BackColor == TimerTheme.TimeInputHint);
    private static bool Plain(TimerCard card) => Parts(card).All(part => part.BackColor == card.TimeInput.BackColor && part.ForeColor == card.TimeInput.ForeColor);
    private static void Key(TextBox part, Keys key) => typeof(Control).GetMethod("OnKeyDown", BindingFlags.Instance | BindingFlags.NonPublic)!
        .Invoke(part, [new KeyEventArgs(key)]);
    private static void Show(Form form)
    {
        form.ShowInTaskbar = false; form.Opacity = 0; form.Show();
        Application.DoEvents(); form.PerformLayout(); Application.DoEvents();
    }
    private static void Picture(Form form, string name)
    {
        Application.DoEvents();
        using var bitmap = new Bitmap(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size));
        bitmap.Save(Path.Combine(output, name + ".png"));
        var inputs = Field<List<TimerCard>>(form, "cards").Select(card => card.TimeInput).ToArray();
        for (var i = 0; i < inputs.Length; i++)
        {
            using var inputImage = new Bitmap(inputs[i].Width, inputs[i].Height);
            inputs[i].DrawToBitmap(inputImage, new Rectangle(Point.Empty, inputImage.Size));
            inputImage.Save(Path.Combine(output, $"{name}-time-{i + 1}.png"));
        }
    }

    [STAThread]
    private static int Main(string[] args)
    {
        output = Path.GetFullPath(args[0]); Directory.CreateDirectory(output);
        Application.SetHighDpiMode(args.Length > 1 && args[1] == "96" ? HighDpiMode.DpiUnaware : HighDpiMode.PerMonitorV2);
        Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
        try
        {
            CheckCards(); CheckStarts();
            File.WriteAllText(Path.Combine(output, "checks.json"), JsonSerializer.Serialize(new { Passed = passed }, new JsonSerializerOptions { WriteIndented = true }));
            return 0;
        }
        catch (Exception ex) { File.WriteAllText(Path.Combine(output, "failure.txt"), ex.ToString()); Console.WriteLine(ex); return 1; }
    }

    private static void CheckCards()
    {
        var store = new SettingsStore(Path.Combine(output, "cards.json"));
        store.Save(new AppSettings { Timers = [new() { Name = "초기 상태" }, new() { Name = "사용자가 지정한 0초" },
            new() { Name = "입력한 시간", DurationSeconds = 150 }] });
        using var main = new MultiTimerForm(store, alarmFactory: () => new Sound());
        Show(main);
        Field<System.Windows.Forms.Timer>(main, "ticker").Stop();
        var cards = Field<List<TimerCard>>(main, "cards");
        var fresh = cards[0];
        Require(cards.All(Highlighted) && cards.All(card => !card.TimeInput.ReadOnly), "Idle timer should highlight regardless of value");
        Require(fresh.TimeInput.Controls.OfType<TimerTimeSeparator>().All(colon => colon.BackColor == TimerTheme.Card), "Colons should have no hint background");
        Picture(main, "initial-versus-explicit-zero");
        File.WriteAllText(Path.Combine(output, "input-layout.json"), JsonSerializer.Serialize(cards.Select(card => new {
            card.Bounds, card.Visible, Input = new { card.TimeInput.Bounds, card.TimeInput.Visible, card.TimeInput.ContentWidth,
                Parts = Parts(card).Select(part => new { part.Bounds, part.Visible, part.Text, BackColor = part.BackColor.ToArgb(), ForeColor = part.ForeColor.ToArgb() }) }
        }), new JsonSerializerOptions { WriteIndented = true }));
        Pass("Initial, saved zero and saved nonzero timers all highlight while idle; colons remain plain");

        var originalBounds = Parts(fresh).Select(part => part.Bounds).ToArray();
        Parts(fresh)[0].Focus(); Application.DoEvents();
        fresh.StartButton.Focus(); Application.DoEvents();
        Require(Highlighted(fresh), "Focus/blur hid idle hint");
        var saves = 0;
        fresh.SaveConfiguration = () => { saves++; store.Save(new AppSettings { Timers = cards.Select(card => card.Configuration).ToList() }); return true; };
        Parts(fresh)[2].Focus(); Application.DoEvents();
        Parts(fresh)[2].Text = "5";
        Require(Highlighted(fresh) && fresh.PendingSeconds == 5 && saves == 0, "Editing hid idle hint or saved early");
        Key(Parts(fresh)[2], Keys.Enter); Application.DoEvents();
        Require(fresh.Configuration.DurationSeconds == 5 && Highlighted(fresh) && saves == 1, "Committing idle input should preserve hint and save once");
        Require(Parts(fresh).Select(part => part.Bounds).SequenceEqual(originalBounds), "Changing hint moves/resizes numeric fields");
        Parts(fresh)[2].Text = "00";
        Require(fresh.CommitSecondsEdit() && fresh.Configuration.DurationSeconds == 0 && Highlighted(fresh), "Explicit zero should highlight while idle");
        Require(store.Load().Timers[0].DurationSeconds == 0, "Explicit zero not persisted");
        Picture(main, "after-explicit-zero");
        Pass("Focus, editing and Enter preserve stopped-state hint without moving fields; explicit zero saves normally");

        Parts(cards[2])[1].Text = "00"; Parts(cards[2])[2].Text = "00";
        Require(cards[2].CommitSecondsEdit() && Highlighted(cards[2]), "Changing idle value to zero hid hint");
        using var restored = new TimerCard(store.Load().Timers[0], 1, alarm: new Sound());
        Require(Highlighted(restored), "Reloaded idle timer should display hint");
        using var untouched = new TimerCard(new TimerSettings(), 1, alarm: new Sound());
        var untouchedSaves = 0; untouched.SaveConfiguration = () => { untouchedSaves++; return true; };
        Require(untouched.CommitSecondsEdit() && untouchedSaves == 0 && Highlighted(untouched), "Programmatic commit changed untouched timer");
        Key(Parts(untouched)[0], Keys.Enter);
        Require(Highlighted(untouched) && untouchedSaves == 0, "Confirming unchanged zero should preserve hint without unnecessary save");
        Pass("Zero edits, settings reload and unchanged-zero confirmation preserve idle hint");

        using var rejected = new TimerCard(new TimerSettings(), 1, alarm: new Sound());
        rejected.SaveConfiguration = () => false;
        Parts(rejected)[2].Text = "5";
        Require(!rejected.CommitSecondsEdit() && rejected.Configuration.DurationSeconds == 0 && Highlighted(rejected),
            "Failed save changed persisted value or stopped-state appearance");
        rejected.SaveConfiguration = () => true;
        Require(rejected.CommitSecondsEdit() && rejected.Configuration.DurationSeconds == 5 && Highlighted(rejected), "Retry did not save stopped input");
        Pass("Failed save rolls back duration; retry saves successfully and keeps stopped-state hint");

        var clock = new Clock();
        using var countdown = new TimerCard(new TimerSettings { DurationSeconds = 2, ReverseAlarm = true }, 1, clock, new Sound());
        countdown.Begin(2);
        Require(Plain(countdown) && countdown.TimeInput.ReadOnly, "Running reverse timer should hide hint");
        clock.Advance(3); countdown.Tick();
        Require(countdown.Session.State == TimerState.Stopped && Highlighted(countdown), "Completed reverse timer should restore hint");
        using var normal = new TimerCard(new TimerSettings { DurationSeconds = 2 }, 1, clock, new Sound());
        normal.Begin(2); clock.Advance(3); normal.Tick();
        Require(normal.Session.State == TimerState.Alarming && Plain(normal) && normal.TimeInput.ReadOnly, "Active normal alarm should hide noneditable hint");
        normal.StopCurrent(); Require(Highlighted(normal), "Stopping normal alarm should restore hint");
        using var newCard = new TimerCard(new TimerSettings(), 4, alarm: new Sound());
        Require(Highlighted(newCard), "A new card inherits another card's entered state");
        main.ClientSize = new Size(560, 690); Application.DoEvents(); Picture(main, "minimum-width");
        Pass("Reverse completion restores hint; active normal alarm hides it until stopped; new timer stays independent");
    }

    private static void CheckStarts()
    {
        var path = Path.Combine(output, "starts.json"); var store = new SettingsStore(path);
        store.Save(new AppSettings { Timers = [new() { Name = "개별 시작", DurationSeconds = 30 }, new() { Name = "대기 상태", DurationSeconds = 60 }] });
        using var main = new MultiTimerForm(store, alarmFactory: () => new Sound());
        Show(main); Field<System.Windows.Forms.Timer>(main, "ticker").Stop();
        Field<ActionChimePlayer>(main, "actionChime").Dispose();
        var cards = Field<List<TimerCard>>(main, "cards");
        var idleBounds = Parts(cards[0]).Select(part => part.Bounds).ToArray();
        Parts(cards[0])[2].Text = "5";
        Call(main, "CycleTimer", cards[0]);
        Require(store.Load().Timers[0].DurationSeconds == 5 && Plain(cards[0]) && Highlighted(cards[1]), "Individual start lost edit or changed another timer's hint");
        Require(Parts(cards[0]).Select(part => part.Bounds).SequenceEqual(idleBounds), "Start changed numeric field layout");
        Picture(main, "one-running-one-idle");
        cards[0].SetHourglassMode(false); Require(Plain(cards[0]), "Numeric-only running view showed hint");
        cards[0].SetHourglassMode(true); Require(Plain(cards[0]), "Hourglass running view showed hint");
        Call(main, "CycleTimer", cards[0]);
        Require(cards.All(Highlighted), "Individual stop did not restore hint");
        Call(main, "CycleAllTimers");
        Require(cards.All(Plain) && cards.All(card => card.TimeInput.ReadOnly), "All-start did not hide all hints");
        Picture(main, "all-running");
        Call(main, "CycleAllTimers");
        Require(cards.All(Highlighted) && cards.All(card => !card.TimeInput.ReadOnly), "All-stop did not restore all hints");
        Picture(main, "all-stopped");
        Pass("Individual start/stop and all-start/all-stop toggle hints per timer; layout and both display modes remain stable");
    }

    private sealed class Sound : IAlarmPlayer
    {
        public bool Start(string path, int volume) => false;
        public void SetVolume(int volume) { }
        public void Stop() { }
        public void Dispose() { }
    }
    private sealed class Clock : TimeProvider
    {
        private long stamp;
        public override long TimestampFrequency => 1000;
        public override long GetTimestamp() => stamp;
        public void Advance(double seconds) => stamp += (long)(seconds * TimestampFrequency);
    }
}
