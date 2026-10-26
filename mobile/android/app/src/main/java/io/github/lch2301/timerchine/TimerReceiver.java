package io.github.lch2301.timerchine;

import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;

/** Receives explicit alarm/control intents and the system's clock/boot events. */
public final class TimerReceiver extends BroadcastReceiver {
    public static final String ACTION_DEADLINE = "io.github.lch2301.timerchine.DEADLINE";
    public static final String ACTION_STOP = "io.github.lch2301.timerchine.STOP";
    public static final String ACTION_STOP_ALL = "io.github.lch2301.timerchine.STOP_ALL";
    public static final String EXTRA_TIMER_ID = "timerId";
    public static final String EXTRA_GENERATION = "generation";

    @Override public void onReceive(Context context, Intent intent) {
        if (intent == null) return;
        String action = intent.getAction();
        if (ACTION_DEADLINE.equals(action)) {
            TimerController.handleDeadline(context, intent.getStringExtra(EXTRA_TIMER_ID),
                    intent.getLongExtra(EXTRA_GENERATION, -1));
        } else if (ACTION_STOP.equals(action)) {
            TimerController.stop(context, intent.getStringExtra(EXTRA_TIMER_ID));
        } else if (ACTION_STOP_ALL.equals(action)) {
            TimerController.stopAll(context);
        } else if (Intent.ACTION_BOOT_COMPLETED.equals(action)) {
            TimerController.onReboot(context);
        } else if (Intent.ACTION_TIME_CHANGED.equals(action)
                || Intent.ACTION_TIMEZONE_CHANGED.equals(action)) {
            TimerController.onClockChanged(context);
        } else if (Intent.ACTION_MY_PACKAGE_REPLACED.equals(action)) {
            TimerController.reconcile(context);
        }
    }
}
