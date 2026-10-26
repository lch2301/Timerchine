package io.github.lch2301.timerchine;

import android.app.Dialog;
import android.graphics.Bitmap;
import android.graphics.Canvas;
import android.os.Looper;
import android.view.View;
import android.view.ViewGroup;
import android.view.MotionEvent;
import android.view.inputmethod.InputConnection;
import android.view.inputmethod.EditorInfo;
import android.widget.Button;
import android.widget.EditText;
import android.widget.RadioButton;
import android.widget.TextView;
import java.io.File;
import java.io.FileOutputStream;
import java.util.zip.ZipEntry;
import java.util.zip.ZipOutputStream;
import java.util.ArrayList;
import java.util.List;
import org.junit.After;
import org.junit.Before;
import org.junit.Test;
import org.junit.runner.RunWith;
import org.robolectric.Robolectric;
import org.robolectric.RobolectricTestRunner;
import org.robolectric.RuntimeEnvironment;
import org.robolectric.Shadows;
import org.robolectric.android.controller.ActivityController;
import org.robolectric.annotation.Config;
import org.robolectric.annotation.GraphicsMode;
import org.robolectric.annotation.LooperMode;
import org.robolectric.shadows.ShadowDialog;
import static org.junit.Assert.*;

/** Regression checks for PC controls and save boundaries, with native Android rendering. */
@RunWith(RobolectricTestRunner.class)
@Config(sdk = 35, qualifiers = "w360dp-h800dp-mdpi", shadows = AtomicFileTestShadow.class)
@GraphicsMode(GraphicsMode.Mode.NATIVE)
@LooperMode(LooperMode.Mode.PAUSED)
public class DesktopParityUiTest {
    private ActivityController<MainActivity> controller;
    private MainActivity activity;
    private View root;

    @Before public void open() throws Exception {
        // Robolectric has compiled Android resources but no installed APK file.
        // Supply the immutable build bytes which AppStore fingerprints on a device.
        File build = new File(RuntimeEnvironment.getApplication().getCacheDir(), "parity-build.apk");
        try (ZipOutputStream zip = new ZipOutputStream(new FileOutputStream(build))) {
            zip.putNextEntry(new ZipEntry("classes.dex"));
            zip.write("PC parity UI test build".getBytes(java.nio.charset.StandardCharsets.UTF_8));
            zip.closeEntry();
            zip.putNextEntry(new ZipEntry("resources.arsc"));
            zip.write("PC parity test resources".getBytes(java.nio.charset.StandardCharsets.UTF_8));
            zip.closeEntry();
        }
        RuntimeEnvironment.getApplication().getApplicationInfo().sourceDir = build.getAbsolutePath();
        RuntimeEnvironment.getApplication().deleteFile("timerchine-state.json");
        RuntimeEnvironment.getApplication().deleteFile("timerchine-state.previous.json");
        RuntimeEnvironment.getApplication().getSharedPreferences("timerchine-runtime", 0).edit().clear().commit();
        controller = Robolectric.buildActivity(MainActivity.class).setup().visible();
        activity = controller.get();
        root = ((ViewGroup) activity.findViewById(android.R.id.content)).getChildAt(0);
        Shadows.shadowOf(Looper.getMainLooper()).idle();
        layout(root, 360, 800);
    }

    @After public void close() {
        Dialog dialog = ShadowDialog.getLatestDialog();
        if (dialog != null && dialog.isShowing()) dialog.dismiss();
        if (controller != null) controller.pause().stop().destroy();
    }

    @Test public void directStoreUpdateKeepsWrittenState() throws Exception {
        Snapshot state=AppStore.load(activity);state.timers.get(0).spec.volume=73;state.timers.get(0).spec.durationSeconds=3723;
        io.github.lch2301.timerchine.core.PresetLibrary.create(state.presets,"새 목록");
        AppStore.save(activity,state);
        assertEquals(73,AppStore.load(activity).timers.get(0).spec.volume);
        assertEquals(3723,AppStore.load(activity).timers.get(0).spec.durationSeconds);
        assertEquals("새 목록",AppStore.load(activity).presets.get(1).name);
    }

    @Test public void tappingEitherDigitAlwaysStartsAtOnesIncludingRepeatedTapsAndSoftKeyboard() {
        List<EditText> fields = timeFields(root);
        for (int index = 0; index < fields.size(); index++) {
            EditText field = fields.get(index);
            for (int position = 0; position < 2; position++) {
                field.setText("34"); field.requestFocus(); field.setSelection(position);
                tap(field, position == 0 ? 1 : field.getWidth() - 1);
                assertEquals(0, field.getSelectionStart()); assertEquals(2, field.getSelectionEnd());
                InputConnection keyboard = field.onCreateInputConnection(new EditorInfo());
                assertNotNull(keyboard);
                keyboard.commitText("1", 1); assertEquals("01", field.getText().toString());
                keyboard.commitText("2", 1); assertEquals("12", field.getText().toString());
                tap(field, 1);
                keyboard.commitText("7", 1); assertEquals("07", field.getText().toString());
                keyboard.commitText("8", 1); assertEquals(index == 0 ? "78" : "59", field.getText().toString());
            }
        }
        EditText seconds = fields.get(2); seconds.performClick();
        seconds.onCreateInputConnection(new EditorInfo()).commitText("01:02:03", 1);
        assertEquals("01", fields.get(0).getText().toString());
        assertEquals("02", fields.get(1).getText().toString());
        assertEquals("03", fields.get(2).getText().toString());
    }

    private static void tap(EditText field, float x) {
        MotionEvent down = MotionEvent.obtain(0, 0, MotionEvent.ACTION_DOWN, x, field.getHeight() / 2f, 0);
        MotionEvent up = MotionEvent.obtain(0, 20, MotionEvent.ACTION_UP, x, field.getHeight() / 2f, 0);
        try { field.dispatchTouchEvent(down); field.dispatchTouchEvent(up); }
        finally { down.recycle(); up.recycle(); }
    }

    @Test public void pcHeaderFooterAndInlineEditorsRemainOnSmallScreen() throws Exception {
        assertNotNull(text(root, "타이머신"));
        assertNotNull(button(root, "타이머 추가"));
        assertNotNull(button(root, "설정"));
        assertNotNull(button(root, "알람 설정"));
        assertNotNull(button(root, "삭제"));
        Button all = button(root, "전체 시작");
        assertNotNull(all);
        int[] footerLocation = new int[2];
        all.getLocationInWindow(footerLocation);
        assertTrue("PC all-action belongs below the timer list", footerLocation[1] > 650);
        assertNull("No unrequested help button", text(root, "?"));
        assertNull("No added subtitle", text(root, "시간을 알리는 두 가지 방법"));
        assertEquals("PC exposes all three time inputs directly", 3, timeFields(root).size());
        capture(root, "main-360x800");
        assertVisibleButtonsFit(root);
    }

    @Test public void alarmSourcesAndVolumeCancelMatchPc() throws Exception {
        button(root, "알람 설정").performClick();
        Dialog dialog = latest();
        View view = dialog.getWindow().getDecorView();
        layoutDialog(view);
        for (String label : new String[]{"일반 알람", "역알람", "내장 멜로디", "내 파일", "외부 링크", "재생 목록"})
            assertTrue(label + " must retain its PC radio control", text(view, label) instanceof RadioButton);
        EditText numeric = null;
        for (View field : descendants(view)) if (field instanceof EditText && "50".contentEquals(((EditText) field).getText())) numeric = (EditText) field;
        assertNotNull("PC numeric volume entry is present beside the slider", numeric);
        numeric.requestFocus();
        numeric.setText("73");
        button(view, "취소").performClick();
        assertFalse(dialog.isShowing());
        assertEquals("Volume is saved independently of alarm settings Cancel", 73, AppStore.load(activity).timers.get(0).spec.volume);
        button(root, "알람 설정").performClick();
        view = latest().getWindow().getDecorView();
        layoutDialog(view);
        capture(view, "alarm-settings-360");
    }

    @Test public void presetCreateSelectsAndClosesOnlyPresetPopup() throws Exception {
        button(root, "알람 설정").performClick();
        Dialog alarm = latest();
        View alarmView = alarm.getWindow().getDecorView();
        ((RadioButton) text(alarmView, "재생 목록")).setChecked(true);
        button(alarmView, "재생 목록 설정").performClick();
        Dialog playlist = latest();
        View playlistView = playlist.getWindow().getDecorView();
        assertNotNull(text(playlistView, "프리셋: 기본 프리셋"));
        assertNotNull(button(playlistView, "파일 추가"));
        button(playlistView, "프리셋").performClick();
        Dialog presets = latest();
        View presetsView = presets.getWindow().getDecorView();
        EditText name = null;
        for (View candidate : descendants(presetsView)) if (candidate instanceof EditText) { name = (EditText) candidate; break; }
        assertNotNull(name);
        name.setText("새 목록");
        button(presetsView, "생성").performClick();
        assertFalse("Creating a preset closes its own popup immediately", presets.isShowing());
        assertTrue("The playlist draft remains open", playlist.isShowing());
        assertNotNull(text(playlistView, "프리셋: 새 목록"));
        assertEquals("Creating a preset does not silently save the playlist draft", 0, AppStore.load(activity).presets.get(1).items.size());
        layoutDialog(playlistView);
        capture(playlistView, "playlist-preset-selected-360");
        button(playlistView,"파일 추가").performClick();
        assertTrue(text(playlistView,"내 파일") instanceof RadioButton);
        assertTrue(text(playlistView,"외부 링크") instanceof RadioButton);
        assertNotNull(button(playlistView,"파일 선택"));
        layoutDialog(playlistView);
        capture(playlistView,"playlist-file-card-360");
        playlist.dismiss();
        alarm.dismiss();
    }

    @Test public void inlineDurationCommitsWithoutAnExtraDialog() throws Exception {
        List<EditText> fields = timeFields(root);
        fields.get(0).requestFocus();
        fields.get(0).setText("01:02:03");
        fields.get(2).onEditorAction(EditorInfo.IME_ACTION_DONE);
        Shadows.shadowOf(Looper.getMainLooper()).idle();
        assertEquals(3723, AppStore.load(activity).timers.get(0).spec.durationSeconds);
        assertNull("Time editing stays inline as on PC", ShadowDialog.getLatestDialog());
        capture(root, "inline-time-360x800");
    }

    @Test public void mobileTimerActionsAndPopupsExcludeShortcutRegistration() {
        assertNotNull("Individual timer control remains available",button(root,"시작"));
        assertNotNull("All-timer control remains available",button(root,"전체 시작"));
        assertNoShortcutControls(root);
        button(root,"타이머 추가").performClick();
        assertEquals("Adding timers keeps all inline time controls",6,timeFields(root).size());
        assertNoShortcutControls(root);

        button(root,"설정").performClick();
        Dialog settings=latest();
        assertNoShortcutControls(settings.getWindow().getDecorView());
        settings.dismiss();

        button(root,"알람 설정").performClick();
        Dialog alarm=latest();
        View alarmView=alarm.getWindow().getDecorView();
        assertNoShortcutControls(alarmView);
        ((RadioButton)text(alarmView,"재생 목록")).setChecked(true);
        button(alarmView,"재생 목록 설정").performClick();
        Dialog playlist=latest();
        View playlistView=playlist.getWindow().getDecorView();
        assertNoShortcutControls(playlistView);
        button(playlistView,"프리셋").performClick();
        Dialog presets=latest();
        assertNoShortcutControls(presets.getWindow().getDecorView());
        presets.dismiss();
        playlist.dismiss();
        alarm.dismiss();
    }

    @Test public void settingsRetainPcSectionsAndGlobalLinkConsent() throws Exception {
        button(root,"설정").performClick();
        View view=latest().getWindow().getDecorView();
        for(String label:new String[]{"모래시계 표시","화면을 닫으면 백그라운드 재생","다운로드 및 개인 알람 이용 권한을 확인했습니다."})
            assertTrue(label,text(view,label) instanceof android.widget.CheckBox);
        assertNotNull(text(view,"외부 링크 이용 안내"));
        assertNotNull(text(view,"글꼴 · 오디오 엔진 라이선스"));
        layoutDialog(view);
        capture(view,"settings-360");
    }

    @Test @Config(qualifiers="w800dp-h360dp-land-mdpi")
    public void landscapePreservesHeaderFooterAndScrollsAlarmSettings() throws Exception {
        layout(root,800,360);
        assertVisibleButtonsFit(root);
        capture(root,"main-800x360");
        button(root,"알람 설정").performClick();
        View view=latest().getWindow().getDecorView();
        view.measure(View.MeasureSpec.makeMeasureSpec(720,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(340,View.MeasureSpec.AT_MOST));
        view.layout(0,0,720,view.getMeasuredHeight());
        assertNotNull(button(view,"저장"));
        assertNotNull(button(view,"취소"));
        capture(view,"alarm-settings-800x360");
    }

    @Test public void activeAndStoppedInputsKeepThePcEditableHintRules() throws Exception {
        Snapshot state=AppStore.load(activity);
        io.github.lch2301.timerchine.core.TimerState timer=state.timers.get(0);
        timer.spec.durationSeconds=60;
        io.github.lch2301.timerchine.core.TimerEngine.start(timer,android.os.SystemClock.elapsedRealtime(),System.currentTimeMillis());
        AppStore.save(activity,state);
        activity.sendBroadcast(new android.content.Intent(TimerController.ACTION_STATE_CHANGED).setPackage(activity.getPackageName()));
        Shadows.shadowOf(Looper.getMainLooper()).idle();
        for(EditText field:timeFields(root)){assertFalse(field.isFocusable());assertNull(field.getBackground());}
        assertNotNull(button(root,"중단"));
        capture(root,"timer-running-360x800");
        state=AppStore.load(activity);timer=state.timers.get(0);
        io.github.lch2301.timerchine.core.TimerEngine.stop(timer,android.os.SystemClock.elapsedRealtime());
        AppStore.save(activity,state);
        activity.sendBroadcast(new android.content.Intent(TimerController.ACTION_STATE_CHANGED).setPackage(activity.getPackageName()));
        Shadows.shadowOf(Looper.getMainLooper()).idle();
        for(EditText field:timeFields(root)){assertTrue(field.isFocusable());assertNotNull(field.getBackground());}
        assertNotNull(button(root,"재시작"));
        capture(root,"timer-stopped-360x800");
    }

    private Dialog latest() { Dialog dialog = ShadowDialog.getLatestDialog(); assertNotNull(dialog); assertTrue(dialog.isShowing()); return dialog; }
    private static TextView text(View root, String value) {
        for (View view : descendants(root)) if (view instanceof TextView && value.contentEquals(((TextView) view).getText())) return (TextView) view;
        return null;
    }
    private static Button button(View root, String value) { TextView view = text(root, value); return view instanceof Button ? (Button) view : null; }
    private static void assertNoShortcutControls(View root) {
        for(View view:descendants(root)) {
            if(view instanceof TextView) {
                String label=((TextView)view).getText().toString();
                assertFalse("Mobile UI must not expose shortcut registration: "+label,label.contains("단축키"));
                assertFalse("Mobile UI must not expose an unassigned-key button: "+label,label.equals("지정 안 함")||label.equals("지정안함"));
            }
            CharSequence description=view.getContentDescription();
            assertFalse("Mobile accessibility labels must not expose shortcut registration",description!=null&&description.toString().contains("단축키"));
        }
    }
    private static List<View> descendants(View view) {
        List<View> result = new ArrayList<>(); result.add(view);
        if (view instanceof ViewGroup) for (int i=0;i<((ViewGroup)view).getChildCount();i++) result.addAll(descendants(((ViewGroup)view).getChildAt(i)));
        return result;
    }
    private static List<EditText> timeFields(View view) {
        List<EditText> result = new ArrayList<>();
        for (View child : descendants(view)) if (child instanceof EditText && child.getContentDescription() != null && child.getContentDescription().toString().matches("타이머 (시간|분|초)")) result.add((EditText) child);
        return result;
    }
    private static void layout(View view, int width, int height) {
        view.measure(View.MeasureSpec.makeMeasureSpec(width,View.MeasureSpec.EXACTLY), View.MeasureSpec.makeMeasureSpec(height,View.MeasureSpec.EXACTLY));
        view.layout(0,0,width,height);
    }
    private static void layoutDialog(View view) {
        view.measure(View.MeasureSpec.makeMeasureSpec(340,View.MeasureSpec.EXACTLY),View.MeasureSpec.makeMeasureSpec(740,View.MeasureSpec.AT_MOST));
        view.layout(0,0,340,view.getMeasuredHeight());
    }
    private static void assertVisibleButtonsFit(View root) {
        for (View view : descendants(root)) if (view instanceof Button && view.isShown()) {
            assertTrue("Button has usable width: " + ((Button)view).getText(), view.getWidth() > 0);
            assertTrue("Button has usable height: " + ((Button)view).getText(), view.getHeight() >= 25);
        }
    }
    private static void capture(View view, String name) throws Exception {
        // A real Window traversal dispatches pre-draw before rendering; centered
        // single-line EditTexts calculate their horizontal text scroll there.
        view.getViewTreeObserver().dispatchOnPreDraw();
        // Capture the settled selection state instead of the first frame of
        // framework animated checkbox and radio-button state drawables.
        for(View child:descendants(view))child.jumpDrawablesToCurrentState();
        Bitmap bitmap = Bitmap.createBitmap(view.getWidth(),view.getHeight(),Bitmap.Config.ARGB_8888);
        view.draw(new Canvas(bitmap));
        File directory = new File(System.getenv().getOrDefault("TIMERCHINE_QA_OUTPUT", "build/reports/desktop-parity"));
        if (!directory.isDirectory() && !directory.mkdirs()) throw new IllegalStateException("Cannot create screenshot directory");
        try (FileOutputStream stream = new FileOutputStream(new File(directory,name+".png"))) { assertTrue(bitmap.compress(Bitmap.CompressFormat.PNG,100,stream)); }
        bitmap.recycle();
    }
}

