package io.github.lch2301.timerchine;

import android.app.AlarmManager;
import android.app.PendingIntent;
import android.content.Context;
import android.content.Intent;
import android.net.Uri;
import android.os.Build;
import android.os.SystemClock;

import io.github.lch2301.timerchine.core.TimerState;

/** System wake-up deadlines; the UI's refresh loop is never the timer clock. */
public final class AlarmScheduler {
    private AlarmScheduler() { }

    public static boolean canScheduleExact(Context context) {
        AlarmManager alarms = context.getSystemService(AlarmManager.class);
        return alarms != null && (Build.VERSION.SDK_INT < 31 || alarms.canScheduleExactAlarms());
    }

    public static boolean schedule(Context context, TimerState state) {
        if (state.phase != TimerState.Phase.RUNNING) return true;
        AlarmManager alarms = context.getSystemService(AlarmManager.class);
        if (alarms == null || !canScheduleExact(context)) return false;
        long remaining = Math.max(1L, state.deadlineElapsedMs - SystemClock.elapsedRealtime());
        long triggerWall = System.currentTimeMillis() + remaining;
        Intent open = new Intent().setClassName(context,
                "io.github.lch2301.timerchine.MainActivity")
                .setAction("io.github.lch2301.timerchine.OPEN_TIMER")
                .setData(Uri.parse("timerchine://timer/" + Uri.encode(state.spec.id)))
                .addFlags(Intent.FLAG_ACTIVITY_CLEAR_TOP | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        PendingIntent show = PendingIntent.getActivity(context, 0, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
        try {
            // Unlike allowWhileIdle, distinct alarm-clock alarms aren't batched by
            // Doze's per-app nine-minute quota. Recomputed from a monotonic deadline.
            alarms.setAlarmClock(new AlarmManager.AlarmClockInfo(triggerWall, show),
                    deadlineIntent(context, state.spec.id, state.generation));
            return true;
        } catch (SecurityException denied) {
            return false;
        }
    }

    public static void cancel(Context context, String timerId) {
        AlarmManager alarms = context.getSystemService(AlarmManager.class);
        if (alarms != null) alarms.cancel(deadlineIntent(context, timerId, 0));
    }

    private static PendingIntent deadlineIntent(Context context, String timerId, long generation) {
        Intent intent = new Intent(context, TimerReceiver.class)
                .setAction(TimerReceiver.ACTION_DEADLINE)
                .setData(Uri.parse("timerchine://deadline/" + Uri.encode(timerId)))
                .putExtra(TimerReceiver.EXTRA_TIMER_ID, timerId)
                .putExtra(TimerReceiver.EXTRA_GENERATION, generation);
        return PendingIntent.getBroadcast(context, 0, intent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }
}
