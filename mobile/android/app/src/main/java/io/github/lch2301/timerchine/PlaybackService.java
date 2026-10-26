package io.github.lch2301.timerchine;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.ServiceInfo;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.graphics.Color;
import android.graphics.Paint;
import android.graphics.drawable.Icon;
import android.media.AudioAttributes;
import android.media.AudioFocusRequest;
import android.media.AudioManager;
import android.media.MediaMetadata;
import android.media.MediaPlayer;
import android.media.session.MediaSession;
import android.media.session.PlaybackState;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.IBinder;
import android.os.Looper;
import android.os.PowerManager;
import android.os.SystemClock;

import java.io.File;
import java.util.ArrayList;
import java.util.Collections;
import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ThreadLocalRandom;
import java.util.concurrent.ConcurrentHashMap;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import io.github.lch2301.timerchine.core.OriginalSounds;

import io.github.lch2301.timerchine.core.TimerEngine;
import io.github.lch2301.timerchine.core.TimerState;
import io.github.lch2301.timerchine.core.Track;

/** Native, notification-controlled playback which outlives the app's screen. */
public final class PlaybackService extends Service {
    private static final String ACTION_REFRESH = "io.github.lch2301.timerchine.REFRESH_PLAYBACK";
    private static final String ACTION_CHIME = "io.github.lch2301.timerchine.ACTION_CHIME";
    private static final String MEDIA_CHANNEL = "timerchine_playback";
    private static final String ALARM_CHANNEL = "timerchine_alarm_playback";
    private static final int NOTIFICATION_ID = 1200;
    private static final long PREPARE_TIMEOUT_MS = 30_000L;
    private static volatile PlaybackService instance;
    private static final Map<String, AudioProgress> progressCache = new ConcurrentHashMap<>();
    private static final ExecutorService mediaWorkers = Executors.newCachedThreadPool();
    private final Handler handler = new Handler(Looper.getMainLooper());
    private final Map<String, Slot> slots = new HashMap<>();
    private MediaSession session;
    private AudioManager audioManager;
    private AudioFocusRequest focusRequest;
    private boolean focusGranted;
    private boolean focusIsAlarm;
    private boolean focusSuspended;
    private int focusGeneration;
    private boolean ducked;
    private boolean destroyed;
    private boolean finishing;
    private boolean tickScheduled;
    private long lastCheckpoint;
    private String notificationSignature = "";
    private MediaPlayer actionChime;

    public static final class AudioProgress {
        public final int positionMs;
        public final int durationMs;
        public final String status;
        private final String sourceKey;
        AudioProgress(int positionMs, int durationMs, String status, String sourceKey) {
            this.positionMs = Math.max(0, positionMs);
            this.durationMs = Math.max(0, durationMs);
            this.status = status;
            this.sourceKey = sourceKey;
        }
        public double fraction() { return durationMs > 0 ? Math.min(1, positionMs / (double) durationMs) : 0; }
        public int remainingSeconds() { return (int) (Math.max(0L, (long) durationMs - positionMs + 999) / 1000); }
    }

    public static AudioProgress audioProgress(Context context, String timerId) {
        TimerState state = TimerController.find(AppStore.load(context), timerId);
        if (state == null) return new AudioProgress(0, 0, "stopped", "");
        PlaybackService running = instance;
        if (running != null && Looper.myLooper() == Looper.getMainLooper()) {
            Slot slot = running.slots.get(timerId);
            if (slot != null) progressCache.put(timerId, slot.progress());
        }
        AudioProgress cached = progressCache.get(timerId);
        String key = state.spec.sourceKey();
        String status = state.playbackFailed ? "deviceError"
                : !TimerEngine.isActive(state) ? "paused" : TimerEngine.isAudioActive(state) ? "preparing" : "stopped";
        if (cached != null && key.equals(cached.sourceKey)) {
            if (!state.playbackFailed && TimerEngine.isAudioActive(state)) status = cached.status;
            return new AudioProgress(cached.positionMs, cached.durationMs, status, key);
        }
        int defaultDuration = !state.spec.usePlaylist && state.spec.soundUri.isEmpty()
                ? OriginalSounds.melodyDurationMs() : 0;
        return new AudioProgress(state.playbackPositionMs, defaultDuration, status, key);
    }

    public static void forgetProgress(String id) { progressCache.remove(id); }

    public static void endSession(Context context) {
        progressCache.clear();
        PlaybackService running = instance;
        if (running != null) {
            Runnable close = () -> { running.releaseChime(); running.finishPlayback(); };
            if (Looper.myLooper() == Looper.getMainLooper()) close.run();
            else running.handler.post(close);
        }
        else context.stopService(new Intent(context, PlaybackService.class));
    }

    public static void playActionChime(Context context, boolean start) {
        PlaybackService running = instance;
        if (running != null && !running.destroyed && !running.finishing) {
            running.handler.post(() -> running.beginChime(start));
        } else {
            try {
                context.getApplicationContext().startForegroundService(new Intent(context, PlaybackService.class)
                        .setAction(ACTION_CHIME).putExtra("start", start));
            } catch (RuntimeException unavailable) { /* Chime failure must not change the timer action. */ }
        }
    }

    private final BroadcastReceiver noisyReceiver = new BroadcastReceiver() {
        @Override public void onReceive(Context context, Intent intent) {
            if (AudioManager.ACTION_AUDIO_BECOMING_NOISY.equals(intent.getAction()))
                interruptPlayback("이어폰 연결이 해제되어 음악을 중단했습니다. 타이머는 계속됩니다.", true);
        }
    };

    private final Runnable ticker = new Runnable() {
        @Override public void run() {
            tickScheduled = false;
            if (destroyed) return;
            TimerController.tick(PlaybackService.this);
            reconcilePlayers();
            if (SystemClock.elapsedRealtime() - lastCheckpoint >= 5_000L) {
                checkpointSlots();
                lastCheckpoint = SystemClock.elapsedRealtime();
            }
            ensureTicker();
        }
    };

    public static void refresh(Context context) {
        PlaybackService running = instance;
        if (running != null && !running.destroyed && !running.finishing) {
            running.handler.post(running::reconcilePlayers);
            return;
        }
        boolean needsAudio = false;
        for (TimerState state : AppStore.load(context).timers) needsAudio |= TimerEngine.isAudioActive(state);
        if (!needsAudio) return;
        try {
            context.getApplicationContext().startForegroundService(new Intent(context, PlaybackService.class)
                    .setAction(ACTION_REFRESH));
        } catch (RuntimeException unavailable) {
            TimerController.serviceStartFailed(context,
                    "백그라운드 재생을 시작하지 못했습니다. 앱을 열어 다시 시작해 주세요.");
        }
    }

    static void captureCursor(String timerId) {
        PlaybackService running = instance;
        if (running == null || running.destroyed || timerId == null) return;
        if (Looper.myLooper() == Looper.getMainLooper()) {
            Slot slot = running.slots.get(timerId);
            if (slot != null) slot.checkpoint();
        } else {
            running.handler.post(() -> {
                Slot slot = running.slots.get(timerId);
                if (slot != null) slot.checkpoint();
            });
        }
    }

    static void captureAllCursors() {
        PlaybackService running = instance;
        if (running == null || running.destroyed) return;
        if (Looper.myLooper() == Looper.getMainLooper()) running.checkpointSlots();
        else running.handler.post(running::checkpointSlots);
    }

    @Override public void onCreate() {
        super.onCreate();
        instance = this;
        audioManager = getSystemService(AudioManager.class);
        createChannels();
        session = new MediaSession(this, "Timerchine");
        session.setCallback(new MediaSession.Callback() {
            @Override public void onPause() { TimerController.stopAll(PlaybackService.this); }
            @Override public void onStop() { TimerController.stopAll(PlaybackService.this); }
            @Override public void onCustomAction(String action, Bundle extras) {
                if (TimerReceiver.ACTION_STOP_ALL.equals(action)) TimerController.stopAll(PlaybackService.this);
            }
        }, handler);
        session.setSessionActivity(openApp());
        session.setActive(true);
        if (Build.VERSION.SDK_INT >= 33) registerReceiver(noisyReceiver,
                new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY), Context.RECEIVER_NOT_EXPORTED);
        else registerReceiver(noisyReceiver, new IntentFilter(AudioManager.ACTION_AUDIO_BECOMING_NOISY));
    }

    @Override public int onStartCommand(Intent intent, int flags, int startId) {
        finishing = false;
        instance = this;
        // A foreground notification is required even while a slow provider prepares audio.
        Notification initial = buildNotification(AppStore.load(this), true);
        notificationSignature = "";
        if (Build.VERSION.SDK_INT >= 29)
            startForeground(NOTIFICATION_ID, initial, ServiceInfo.FOREGROUND_SERVICE_TYPE_MEDIA_PLAYBACK);
        else startForeground(NOTIFICATION_ID, initial);
        if (intent != null && ACTION_CHIME.equals(intent.getAction())) beginChime(intent.getBooleanExtra("start", true));
        TimerController.tick(this);
        reconcilePlayers();
        handler.removeCallbacks(ticker);
        tickScheduled = false;
        ensureTicker();
        return slots.isEmpty() ? START_NOT_STICKY : START_STICKY;
    }

    @Override public IBinder onBind(Intent intent) { return null; }

    @Override public void onTaskRemoved(Intent rootIntent) {
        // Leaving or dismissing the screen is the mobile equivalent of tray minimization.
        // An explicit stop action, not the Activity lifetime, ends playback.
        super.onTaskRemoved(rootIntent);
    }

    private void reconcilePlayers() {
        if (destroyed || finishing) return;
        Snapshot snapshot = AppStore.load(this);
        Set<String> active = new HashSet<>();
        boolean alarm = false;
        for (TimerState state : snapshot.timers) {
            if (!TimerEngine.isAudioActive(state)) continue;
            active.add(state.spec.id);
            alarm |= !state.spec.reverse;
        }
        for (String id : new ArrayList<>(slots.keySet())) {
            TimerState state = TimerController.find(snapshot, id);
            Slot slot = slots.get(id);
            if (!active.contains(id) || state == null || !slot.sourceKey.equals(state.spec.sourceKey())
                    || (!state.spec.reverse && slot.generation != state.generation)) {
                slot.checkpoint();
                slot.release();
                slots.remove(id);
            }
        }
        if (active.isEmpty()) {
            finishPlayback();
            return;
        }
        if (!obtainFocus(alarm)) {
            interruptPlayback("다른 앱이 오디오를 사용 중입니다. 타이머는 계속되며 다시 시작하면 음악을 재시도합니다.", false);
            return;
        }
        for (TimerState state : snapshot.timers) {
            if (!TimerEngine.isAudioActive(state)) continue;
            Slot slot = slots.get(state.spec.id);
            if (slot == null) {
                slot = new Slot(state);
                slots.put(state.spec.id, slot);
                slot.prepare();
            } else {
                slot.state = state.copy();
                slot.generation = state.generation;
                slot.applyVolume();
                if (slot.player == null) slot.prepare();
            }
        }
        boolean playing = anyPlayerPlaying();
        StringBuilder signature = new StringBuilder(playing ? "playing|" : "preparing|");
        for (TimerState state : snapshot.timers) if (TimerEngine.isAudioActive(state))
            signature.append(state.spec.id).append('|').append(state.spec.name).append('|').append(state.phase).append('|');
        if (!notificationSignature.contentEquals(signature)) {
            notificationSignature = signature.toString();
            updateSession(snapshot, playing);
            getSystemService(NotificationManager.class).notify(NOTIFICATION_ID, buildNotification(snapshot, !playing));
        }
        ensureTicker();
    }

    private void ensureTicker() {
        if (!destroyed && !slots.isEmpty() && !tickScheduled) {
            tickScheduled = true;
            handler.postDelayed(ticker, 250L);
        }
    }

    private AudioAttributes attributes(boolean alarm) {
        return new AudioAttributes.Builder()
                .setUsage(alarm ? AudioAttributes.USAGE_ALARM : AudioAttributes.USAGE_MEDIA)
                .setContentType(alarm ? AudioAttributes.CONTENT_TYPE_SONIFICATION : AudioAttributes.CONTENT_TYPE_MUSIC)
                .build();
    }

    private boolean obtainFocus(boolean alarm) {
        if ((focusGranted || focusSuspended) && focusIsAlarm == alarm) return true;
        if (audioManager == null) return false;
        if (focusRequest != null) audioManager.abandonAudioFocusRequest(focusRequest);
        focusGranted = false;
        focusSuspended = false;
        ducked = false;
        focusIsAlarm = alarm;
        int requestGeneration = ++focusGeneration;
        focusRequest = new AudioFocusRequest.Builder(alarm
                ? AudioManager.AUDIOFOCUS_GAIN_TRANSIENT : AudioManager.AUDIOFOCUS_GAIN)
                .setAudioAttributes(attributes(alarm))
                .setAcceptsDelayedFocusGain(false)
                .setWillPauseWhenDucked(false)
                .setOnAudioFocusChangeListener(change -> {
                    if (destroyed || requestGeneration != focusGeneration) return;
                    if (change == AudioManager.AUDIOFOCUS_GAIN) {
                        focusGranted = true;
                        focusSuspended = false;
                        ducked = false;
                        TimerController.tick(this);
                        for (Slot slot : slots.values()) {
                            slot.applyVolume();
                            if (slot.prepared && !slot.seeking && slot.isCurrent()) slot.startPrepared(slot.player);
                        }
                        handler.post(this::reconcilePlayers);
                    } else if (change == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT_CAN_DUCK) {
                        ducked = true;
                        for (Slot slot : slots.values()) slot.applyVolume();
                    } else if (change == AudioManager.AUDIOFOCUS_LOSS_TRANSIENT) {
                        focusGranted = false;
                        focusSuspended = true;
                        releaseChime();
                        for (Slot slot : slots.values()) if (slot.prepared && slot.player != null) {
                            try { slot.player.pause(); } catch (IllegalStateException ignored) { }
                            slot.checkpoint();
                        }
                        handler.post(this::reconcilePlayers);
                    } else if (change == AudioManager.AUDIOFOCUS_LOSS) {
                        focusGranted = false;
                        focusSuspended = false;
                        releaseChime();
                        interruptPlayback("다른 앱이 오디오를 사용하여 재생을 중단했습니다. 타이머는 계속되며 다시 시작하면 음악을 재시도합니다.", false);
                    }
                }, handler).build();
        focusGranted = audioManager.requestAudioFocus(focusRequest) == AudioManager.AUDIOFOCUS_REQUEST_GRANTED;
        session.setPlaybackToLocal(attributes(alarm));
        return focusGranted;
    }

    private void interruptPlayback(String message, boolean reverseOnly) {
        checkpointSlots();
        Snapshot snapshot = AppStore.load(this);
        for (TimerState state : snapshot.timers) {
            if (TimerEngine.isAudioActive(state) && (!reverseOnly || state.spec.reverse))
                TimerController.audioError(this, state.spec.id, state.generation, message, true);
        }
        reconcilePlayers();
    }

    private void checkpointSlots() {
        for (Slot slot : new ArrayList<>(slots.values())) slot.checkpoint();
    }

    private void beginChime(boolean start) {
        if (destroyed || finishing) return;
        releaseChime();
        if (focusSuspended || (!focusGranted && !obtainFocus(false))) return;
        MediaPlayer chime = new MediaPlayer();
        actionChime = chime;
        chime.setAudioAttributes(new AudioAttributes.Builder()
                .setUsage(AudioAttributes.USAGE_ASSISTANCE_SONIFICATION)
                .setContentType(AudioAttributes.CONTENT_TYPE_SONIFICATION).build());
        chime.setWakeMode(this, PowerManager.PARTIAL_WAKE_LOCK);
        chime.setOnPreparedListener(player -> {
            if (player != actionChime || destroyed) return;
            try { player.start(); } catch (IllegalStateException unavailable) {
                releaseChime(); handler.post(this::reconcilePlayers);
            }
        });
        chime.setOnCompletionListener(player -> {
            if (player == actionChime) { releaseChime(); handler.post(this::reconcilePlayers); }
        });
        chime.setOnErrorListener((player, what, extra) -> {
            if (player == actionChime) { releaseChime(); handler.post(this::reconcilePlayers); }
            return true;
        });
        try {
            chime.setDataSource(this, BuiltinSounds.action(this, start));
            chime.prepareAsync();
        } catch (Exception unavailable) {
            releaseChime(); handler.post(this::reconcilePlayers);
        }
    }

    private void releaseChime() {
        MediaPlayer old = actionChime;
        actionChime = null;
        if (old != null) {
            old.setOnCompletionListener(null);
            old.setOnPreparedListener(null);
            old.setOnErrorListener(null);
            old.release();
        }
    }

    private void finishPlayback() {
        if (actionChime != null) return;
        if (finishing) return;
        finishing = true;
        if (instance == this) instance = null;
        for (Slot slot : new ArrayList<>(slots.values())) slot.release();
        slots.clear();
        if (focusRequest != null && audioManager != null) audioManager.abandonAudioFocusRequest(focusRequest);
        focusRequest = null;
        focusGeneration++;
        focusGranted = false;
        focusSuspended = false;
        ducked = false;
        if (session != null) session.setPlaybackState(new PlaybackState.Builder()
                .setState(PlaybackState.STATE_STOPPED, PlaybackState.PLAYBACK_POSITION_UNKNOWN, 0).build());
        handler.removeCallbacks(ticker);
        tickScheduled = false;
        stopForeground(STOP_FOREGROUND_REMOVE);
        stopSelf();
    }

    private void createChannels() {
        NotificationManager manager = getSystemService(NotificationManager.class);
        NotificationChannel media = new NotificationChannel(MEDIA_CHANNEL,
                "백그라운드 음악 재생", NotificationManager.IMPORTANCE_LOW);
        media.setDescription("역알람의 재생 상태와 중단 버튼");
        media.setSound(null, null);
        NotificationChannel alarm = new NotificationChannel(ALARM_CHANNEL,
                "타이머 알람", NotificationManager.IMPORTANCE_HIGH);
        alarm.setDescription("시간이 종료된 타이머의 알람 상태와 중단 버튼");
        alarm.setSound(null, null); // MediaPlayer owns the sound and per-timer volume.
        manager.createNotificationChannel(media);
        manager.createNotificationChannel(alarm);
    }

    private Notification buildNotification(Snapshot snapshot, boolean preparing) {
        List<TimerState> audio = new ArrayList<>();
        boolean alarm = false;
        for (TimerState state : snapshot.timers) if (TimerEngine.isAudioActive(state)) {
            audio.add(state);
            alarm |= !state.spec.reverse;
        }
        String title = audio.size() == 1 ? audio.get(0).spec.name : "타이머신";
        String text = preparing ? "오디오 재생 준비 중" : audio.size() > 1
                ? audio.size() + "개 타이머의 오디오 재생 중"
                : audio.isEmpty() ? "동작 알림음" : focusSuspended ? "다른 오디오가 끝나면 음악이 이어집니다"
                : alarm ? "설정한 시간이 되었습니다" : "역알람 음악 재생 중";
        Notification.Builder builder = new Notification.Builder(this, alarm ? ALARM_CHANNEL : MEDIA_CHANNEL)
                .setSmallIcon(R.drawable.ic_notification)
                .setContentTitle(title).setContentText(text)
                .setContentIntent(openApp()).setShowWhen(false).setOnlyAlertOnce(true)
                .setOngoing(true).setVisibility(Notification.VISIBILITY_PUBLIC)
                .setCategory(alarm ? Notification.CATEGORY_ALARM : Notification.CATEGORY_TRANSPORT)
                .setColor(Color.rgb(135, 158, 179));
        int actions = 0;
        if (audio.size() == 1) {
            builder.addAction(new Notification.Action.Builder(stopIcon(), "중단",
                    stopIntent(audio.get(0).spec.id)).build());
            actions++;
        }
        builder.addAction(new Notification.Action.Builder(stopIcon(), "전체 중단", stopAllIntent()).build());
        Notification.MediaStyle style = new Notification.MediaStyle().setMediaSession(session.getSessionToken());
        if (actions == 1) style.setShowActionsInCompactView(0, 1);
        else style.setShowActionsInCompactView(0);
        return builder.setStyle(style).build();
    }

    private Icon stopIcon() {
        Bitmap bitmap = Bitmap.createBitmap(48, 48, Bitmap.Config.ARGB_8888);
        Paint paint = new Paint(Paint.ANTI_ALIAS_FLAG);
        paint.setColor(Color.rgb(155, 176, 194));
        new Canvas(bitmap).drawRect(10, 10, 38, 38, paint);
        return Icon.createWithBitmap(bitmap);
    }

    private PendingIntent openApp() {
        Intent open = new Intent().setClassName(this, "io.github.lch2301.timerchine.MainActivity")
                .addFlags(Intent.FLAG_ACTIVITY_SINGLE_TOP | Intent.FLAG_ACTIVITY_CLEAR_TOP);
        return PendingIntent.getActivity(this, 0, open,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }

    private PendingIntent stopIntent(String timerId) {
        Intent intent = new Intent(this, TimerReceiver.class).setAction(TimerReceiver.ACTION_STOP)
                .setData(Uri.parse("timerchine://stop/" + Uri.encode(timerId)))
                .putExtra(TimerReceiver.EXTRA_TIMER_ID, timerId);
        return PendingIntent.getBroadcast(this, 0, intent,
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }

    private PendingIntent stopAllIntent() {
        return PendingIntent.getBroadcast(this, 0,
                new Intent(this, TimerReceiver.class).setAction(TimerReceiver.ACTION_STOP_ALL),
                PendingIntent.FLAG_UPDATE_CURRENT | PendingIntent.FLAG_IMMUTABLE);
    }

    private boolean anyPlayerPlaying() {
        for (Slot slot : slots.values()) {
            if (slot.player == null || !slot.prepared) continue;
            try { if (slot.player.isPlaying()) return true; } catch (IllegalStateException ignored) { }
        }
        return false;
    }

    private void updateSession(Snapshot snapshot, boolean playing) {
        String title = "타이머신";
        for (TimerState state : snapshot.timers) if (TimerEngine.isAudioActive(state)) {
            title = state.spec.name;
            break;
        }
        session.setMetadata(new MediaMetadata.Builder()
                .putString(MediaMetadata.METADATA_KEY_TITLE, title)
                .putString(MediaMetadata.METADATA_KEY_ARTIST, "타이머신").build());
        session.setPlaybackState(new PlaybackState.Builder()
                .setActions(PlaybackState.ACTION_PAUSE | PlaybackState.ACTION_STOP)
                .addCustomAction(TimerReceiver.ACTION_STOP_ALL, "전체 중단", R.drawable.ic_stop)
                .setState(focusSuspended ? PlaybackState.STATE_PAUSED : playing ? PlaybackState.STATE_PLAYING : PlaybackState.STATE_BUFFERING,
                        PlaybackState.PLAYBACK_POSITION_UNKNOWN, playing ? 1f : 0f).build());
    }

    @Override public void onDestroy() {
        destroyed = true;
        releaseChime();
        checkpointSlots();
        for (Slot slot : new ArrayList<>(slots.values())) slot.release();
        slots.clear();
        handler.removeCallbacksAndMessages(null);
        try { unregisterReceiver(noisyReceiver); } catch (IllegalArgumentException ignored) { }
        if (focusRequest != null && audioManager != null) audioManager.abandonAudioFocusRequest(focusRequest);
        focusGeneration++;
        if (session != null) { session.setActive(false); session.release(); }
        if (instance == this) instance = null;
        super.onDestroy();
    }

    private final class Slot {
        final String id;
        final String sourceKey;
        TimerState state;
        long generation;
        List<Track> tracks = new ArrayList<>();
        List<Integer> order = new ArrayList<>();
        int cursor;
        int resumePosition;
        int failedTracks;
        boolean fallback;
        boolean prepared;
        boolean seeking;
        boolean substituted;
        int durationHintMs;
        boolean decodedFallback;
        boolean resolving;
        String resolvedForUri;
        String resolvedPath;
        ExternalMedia.CancellationToken pendingToken;
        MediaPlayer player;
        Runnable timeout;

        Slot(TimerState state) {
            this.state = state.copy();
            id = state.spec.id;
            sourceKey = state.spec.sourceKey();
            generation = state.generation;
            if (state.spec.usePlaylist && state.spec.playlist != null) {
                for (Track track : state.spec.playlist) if (track != null) tracks.add(track.copy());
            } else if (state.spec.soundUri != null && !state.spec.soundUri.isEmpty()) {
                Track track = new Track();
                track.uri = state.spec.soundUri;
                track.title = state.spec.soundTitle;
                track.remote = ExternalMedia.isLink(track.uri);
                tracks.add(track);
            }
            for (int i = 0; i < tracks.size(); i++) order.add(i);
            boolean savedSource = sourceKey.equals(state.playbackSourceKey);
            if (savedSource && validOrder(state.playbackOrder)) order = new ArrayList<>(state.playbackOrder);
            else if (state.spec.shuffle && state.spec.usePlaylist) Collections.shuffle(order);
            cursor = savedSource && !order.isEmpty() ? Math.max(0, state.playlistIndex) % order.size() : 0;
            resumePosition = savedSource ? Math.max(0, state.playbackPositionMs) : 0;
            if (tracks.isEmpty()) {
                fallback = true;
                substituted = state.spec.usePlaylist || !state.spec.soundUri.isEmpty();
                durationHintMs = OriginalSounds.melodyDurationMs();
                if (state.spec.usePlaylist) report("재생 목록이 비어 있어 기본 알람음을 재생합니다.", false);
            }
        }

        private boolean validOrder(List<Integer> candidate) {
            if (candidate == null || candidate.size() != tracks.size()) return false;
            Set<Integer> unique = new HashSet<>(candidate);
            if (unique.size() != tracks.size()) return false;
            for (int i = 0; i < tracks.size(); i++) if (!unique.contains(i)) return false;
            return true;
        }

        void prepare() {
            if (resolving) return;
            releasePlayer();
            if (!isCurrent()) return;
            MediaPlayer next = new MediaPlayer();
            player = next;
            prepared = false;
            next.setAudioAttributes(attributes(!state.spec.reverse));
            next.setWakeMode(PlaybackService.this, PowerManager.PARTIAL_WAKE_LOCK);
            next.setOnPreparedListener(mp -> {
                if (mp != player) return;
                if (!isCurrent()) { releasePlayer(); return; }
                if (!fallback && !decodedFallback && requiresAudioOnlyExtraction(mp)) {
                    Track track = tracks.get(order.get(cursor));
                    String input = track.uri.equals(resolvedForUri) && resolvedPath != null
                            ? Uri.fromFile(new File(resolvedPath)).toString() : track.uri;
                    releasePlayer();
                    decodedFallback = true;
                    resolveSource(input, false, track.uri);
                    return;
                }
                if (timeout != null) handler.removeCallbacks(timeout);
                prepared = true;
                try { durationHintMs = Math.max(0, mp.getDuration()); } catch (IllegalStateException ignored) { }
                applyVolume();
                try {
                    if (resumePosition > 0) {
                        seeking = true;
                        int duration = mp.getDuration();
                        int position = duration > 0 ? Math.min(resumePosition, Math.max(0, duration - 1)) : resumePosition;
                        mp.setOnSeekCompleteListener(ready -> {
                            seeking = false;
                            ready.setOnSeekCompleteListener(null);
                            if (ready == player && isCurrent()) startPrepared(ready);
                        });
                        mp.seekTo(position, MediaPlayer.SEEK_CLOSEST);
                    } else startPrepared(mp);
                } catch (RuntimeException invalidMedia) { failTrack(); }
            });
            next.setOnCompletionListener(mp -> {
                if (mp != player || !isCurrent()) return;
                resumePosition = 0;
                if (!order.isEmpty() && !fallback) advanceAfterCompletion();
                resolvedForUri = null;
                resolvedPath = null;
                decodedFallback = false;
                releasePlayer();
                checkpoint();
                prepare();
            });
            next.setOnErrorListener((mp, what, extra) -> {
                if (mp == player) handler.post(() -> { if (mp == player) failTrack(); });
                return true;
            });
            timeout = () -> { if (player == next && !prepared && isCurrent()) failTrack(); };
            handler.postDelayed(timeout, PREPARE_TIMEOUT_MS);
            try {
                Uri uri;
                if (fallback) {
                    uri = BuiltinSounds.melody(PlaybackService.this);
                    durationHintMs = OriginalSounds.melodyDurationMs();
                } else {
                    Track track = tracks.get(order.get(cursor));
                    if (track.uri.equals(resolvedForUri) && resolvedPath != null) {
                        uri = Uri.fromFile(new File(resolvedPath));
                    } else if (ExternalMedia.isLink(track.uri)) {
                        if (!AppStore.load(PlaybackService.this).externalLinksConfirmed)
                            throw new IllegalStateException("외부 링크 이용 안내를 확인해 주세요.");
                        ExternalMedia.Prepared cached = ExternalMedia.getPrepared(PlaybackService.this, track.uri);
                        if (cached == null) {
                            releasePlayer();
                            resolveSource(track.uri, true, track.uri);
                            return;
                        }
                        resolvedForUri = track.uri;
                        resolvedPath = cached.path;
                        durationHintMs = durationMillis(cached.durationSeconds);
                        uri = Uri.fromFile(new File(cached.path));
                    } else uri = Uri.parse(track.uri);
                    String scheme = uri.getScheme();
                    if (!("content".equalsIgnoreCase(scheme) || "android.resource".equalsIgnoreCase(scheme)
                            || "https".equalsIgnoreCase(scheme) || isPrivateFile(uri)))
                        throw new IllegalArgumentException("Unsupported audio URI");
                }
                if (uri == null) throw new IllegalArgumentException("No default alarm sound");
                next.setDataSource(PlaybackService.this, uri);
                next.setLooping(!state.spec.usePlaylist || fallback);
                next.prepareAsync();
            } catch (Exception unavailableSource) {
                handler.post(() -> { if (player == next) failTrack(); });
            }
        }

        private boolean isPrivateFile(Uri uri) throws Exception {
            if (!"file".equalsIgnoreCase(uri.getScheme()) || uri.getPath() == null) return false;
            String path = new File(uri.getPath()).getCanonicalPath();
            return path.startsWith(getFilesDir().getCanonicalPath() + File.separator)
                    || path.startsWith(getCacheDir().getCanonicalPath() + File.separator);
        }

        private boolean requiresAudioOnlyExtraction(MediaPlayer media) {
            try {
                int audioTracks = 0;
                for (MediaPlayer.TrackInfo track : media.getTrackInfo()) {
                    if (track.getTrackType() == MediaPlayer.TrackInfo.MEDIA_TRACK_TYPE_VIDEO) return true;
                    if (track.getTrackType() == MediaPlayer.TrackInfo.MEDIA_TRACK_TYPE_AUDIO) audioTracks++;
                }
                return audioTracks > 1;
            } catch (IllegalStateException unavailableMetadata) { return false; }
        }

        private void advanceAfterCompletion() {
            int finishedTrack = order.get(cursor);
            cursor++;
            if (cursor < order.size()) return;
            cursor = 0;
            if (state.spec.shuffle && order.size() > 1) {
                Collections.shuffle(order, ThreadLocalRandom.current());
                // A new shuffle cycle must not immediately repeat the track
                // which just ended. Keep cursor zero for persisted resume state.
                if (order.get(0) == finishedTrack)
                    Collections.swap(order, 0, ThreadLocalRandom.current().nextInt(1, order.size()));
            }
        }

        private void startPrepared(MediaPlayer ready) {
            try {
                if (!focusGranted || !isCurrent()) return;
                ready.start();
                failedTracks = 0;
                resumePosition = 0;
                checkpoint();
                handler.post(PlaybackService.this::reconcilePlayers);
            } catch (RuntimeException invalidPlayer) { failTrack(); }
        }

        private void failTrack() {
            if (!isCurrent()) { releasePlayer(); return; }
            releasePlayer();
            resumePosition = 0;
            if (fallback) {
                report("오디오 장치를 확인해 주세요. 타이머는 계속 실행됩니다.", true);
                handler.post(PlaybackService.this::reconcilePlayers);
                return;
            }
            if (!decodedFallback) {
                Track track = tracks.get(order.get(cursor));
                String input = track.uri.equals(resolvedForUri) && resolvedPath != null
                        ? Uri.fromFile(new File(resolvedPath)).toString() : track.uri;
                decodedFallback = true;
                resolveSource(input, false, track.uri);
                return;
            }
            failedTracks++;
            if (failedTracks < tracks.size() && state.spec.usePlaylist) {
                cursor = (cursor + 1) % order.size();
                resolvedForUri = null;
                resolvedPath = null;
                decodedFallback = false;
                report("열 수 없는 음원을 건너뛰었습니다. 파일 위치 또는 링크를 확인해 주세요.", false);
            } else {
                fallback = true;
                substituted = true;
                report("선택한 음원을 재생할 수 없어 기본 알람음으로 대체했습니다.", false);
            }
            handler.post(this::prepare);
        }

        private void resolveSource(String input, boolean remote, String original) {
            if (resolving || !isCurrent()) return;
            resolving = true;
            ExternalMedia.CancellationToken token = new ExternalMedia.CancellationToken();
            pendingToken = token;
            mediaWorkers.execute(() -> {
                try {
                    ExternalMedia.Prepared result = remote
                            ? ExternalMedia.prepare(PlaybackService.this, input, token, null)
                            : ExternalMedia.prepareLocalUri(PlaybackService.this, input, token, null);
                    handler.post(() -> {
                        if (pendingToken != token || token.isCancelled() || !isCurrent()) return;
                        pendingToken = null;
                        resolving = false;
                        resolvedForUri = original;
                        resolvedPath = result.path;
                        durationHintMs = durationMillis(result.durationSeconds);
                        prepare();
                    });
                } catch (Exception error) {
                    handler.post(() -> {
                        if (pendingToken != token || token.isCancelled() || !isCurrent()) return;
                        pendingToken = null;
                        resolving = false;
                        decodedFallback = true;
                        failTrack();
                    });
                }
            });
        }

        private int durationMillis(double seconds) {
            return Double.isFinite(seconds) && seconds > 0 ? (int) Math.min(Integer.MAX_VALUE, seconds * 1000) : 0;
        }

        void applyVolume() {
            if (player == null || !prepared) return;
            float volume = Math.max(0, Math.min(100, state.spec.volume)) / 100f;
            if (ducked) volume *= 0.2f;
            try { player.setVolume(volume, volume); } catch (IllegalStateException ignored) { }
        }

        boolean isCurrent() {
            if (destroyed || slots.get(id) != this) return false;
            TimerState latest = TimerController.find(AppStore.load(PlaybackService.this), id);
            if (latest == null || !TimerEngine.isAudioActive(latest)
                    || !sourceKey.equals(latest.spec.sourceKey())) return false;
            if (latest.generation != generation) {
                if (!latest.spec.reverse) return false;
                // PC Begin pauses/resumes the same reverse PlaybackRun, including
                // an unfinished prepare. Adopt a restart before its queued UI
                // reconciliation so a completed worker cannot strand resolving=true.
                generation = latest.generation;
                state = latest.copy();
            }
            return true;
        }

        void checkpoint() {
            progressCache.put(id, progress());
            int position = resumePosition;
            if (player != null && prepared) {
                try { position = player.getCurrentPosition(); } catch (IllegalStateException ignored) { }
            }
            // A fallback is not the user's selected track; restart should retry that track.
            TimerController.checkpoint(PlaybackService.this, id, generation, sourceKey,
                    fallback ? 0 : position, cursor, order);
        }

        AudioProgress progress() {
            int position = resumePosition;
            int duration = durationHintMs;
            if (player != null && prepared) {
                try {
                    position = player.getCurrentPosition();
                    duration = Math.max(0, player.getDuration());
                } catch (IllegalStateException ignored) { }
            }
            String status = focusSuspended ? "paused" : !prepared || seeking ? "preparing"
                    : substituted ? "fallback" : "playing";
            return new AudioProgress(position, duration, status, sourceKey);
        }

        private void report(String message, boolean stop) {
            TimerController.audioError(PlaybackService.this, id, generation, message, stop);
        }

        private void releasePlayer() {
            if (timeout != null) handler.removeCallbacks(timeout);
            timeout = null;
            MediaPlayer old = player;
            player = null;
            prepared = false;
            seeking = false;
            if (old != null) {
                old.setOnPreparedListener(null);
                old.setOnCompletionListener(null);
                old.setOnErrorListener(null);
                old.setOnSeekCompleteListener(null);
                old.release();
            }
        }

        void release() {
            if (pendingToken != null) pendingToken.cancel();
            pendingToken = null;
            resolving = false;
            releasePlayer();
        }
    }
}
