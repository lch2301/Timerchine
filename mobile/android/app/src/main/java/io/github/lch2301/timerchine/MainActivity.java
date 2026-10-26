package io.github.lch2301.timerchine;

import android.Manifest;
import android.annotation.SuppressLint;
import android.app.Activity;
import android.app.AlertDialog;
import android.content.BroadcastReceiver;
import android.content.Context;
import android.content.Intent;
import android.content.IntentFilter;
import android.content.pm.PackageManager;
import android.database.Cursor;
import android.graphics.Bitmap;
import android.graphics.BitmapFactory;
import android.graphics.Color;
import android.graphics.Rect;
import android.net.Uri;
import android.os.Build;
import android.os.Bundle;
import android.os.Handler;
import android.os.Looper;
import android.os.SystemClock;
import android.provider.OpenableColumns;
import android.provider.Settings;
import android.text.Editable;
import android.text.InputFilter;
import android.text.InputType;
import android.text.TextUtils;
import android.text.TextWatcher;
import android.view.Gravity;
import android.view.KeyEvent;
import android.view.MotionEvent;
import android.view.View;
import android.view.ViewGroup;
import android.view.WindowInsets;
import android.view.inputmethod.EditorInfo;
import android.view.inputmethod.InputMethodManager;
import android.widget.Button;
import android.widget.CheckBox;
import android.widget.EditText;
import android.widget.ImageView;
import android.widget.LinearLayout;
import android.widget.ListView;
import android.widget.ArrayAdapter;
import android.widget.RadioButton;
import android.widget.RadioGroup;
import android.widget.ScrollView;
import android.widget.SeekBar;
import android.widget.TextView;
import android.widget.Toast;
import io.github.lch2301.timerchine.core.PlaylistPreset;
import io.github.lch2301.timerchine.core.PresetLibrary;
import io.github.lch2301.timerchine.core.TimerEngine;
import io.github.lch2301.timerchine.core.TimerSpec;
import io.github.lch2301.timerchine.core.TimerState;
import io.github.lch2301.timerchine.core.Track;
import java.io.ByteArrayOutputStream;
import java.io.File;
import java.io.FileInputStream;
import java.io.InputStream;
import java.io.OutputStream;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.LinkedHashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.function.Consumer;

/** Android rendering of the desktop 1.2.0 controls and dialog hierarchy. */
public final class MainActivity extends Activity {
    private static final int PICK_FILE=41,NOTIFICATIONS=42,EXPORT_FILE=43;
    private final Handler handler=new Handler(Looper.getMainLooper());
    private final ExecutorService mediaWork=Executors.newFixedThreadPool(3);
    private final Map<String,Card> cards=new LinkedHashMap<>();
    private final Set<Popup> openPopups=new LinkedHashSet<>();
    private DesktopTheme ui;
    private LinearLayout root,cardList;
    private TextView summary,notice;
    private Button all;
    private Snapshot snapshot=new Snapshot();
    private boolean resumed,receiverRegistered,closing;
    private Runnable pendingStart;
    private Consumer<List<Track>> pickerCallback;
    private Popup pickerOwner;
    private String exportPath="";
    private AlarmEditor activeAlarmEditor;
    private android.window.OnBackInvokedCallback backCallback;
    private final Runnable ticker=new Runnable(){@Override public void run(){if(resumed){refresh();handler.postDelayed(this,250);}}};
    private final BroadcastReceiver receiver=new BroadcastReceiver(){@Override public void onReceive(Context c,Intent i){refresh();}};

    @Override public void onCreate(Bundle state){
        super.onCreate(state);ui=new DesktopTheme(this);
        getWindow().setStatusBarColor(Color.TRANSPARENT);getWindow().setNavigationBarColor(Color.TRANSPARENT);
        if(Build.VERSION.SDK_INT>=30)getWindow().setDecorFitsSystemWindows(false);
        root=ui.column();root.setBackgroundColor(DesktopTheme.BACKGROUND);root.setFocusableInTouchMode(true);
        root.setPadding(dp(12),dp(12),dp(12),dp(12));
        root.setOnApplyWindowInsetsListener((view,insets)->{
            int top,bottom,left,right;
            if(Build.VERSION.SDK_INT>=30){android.graphics.Insets b=insets.getInsets(WindowInsets.Type.systemBars()|WindowInsets.Type.displayCutout());top=b.top;bottom=Math.max(b.bottom,insets.getInsets(WindowInsets.Type.ime()).bottom);left=b.left;right=b.right;}
            else{top=insets.getSystemWindowInsetTop();bottom=insets.getSystemWindowInsetBottom();left=insets.getSystemWindowInsetLeft();right=insets.getSystemWindowInsetRight();}
            view.setPadding(dp(12)+left,dp(12)+top,dp(12)+right,dp(12)+bottom);return insets;
        });
        LinearLayout header=ui.row();header.addView(new DesktopLogoView(this),new LinearLayout.LayoutParams(dp(38),dp(38)));space(header,8);
        TextView title=ui.text("타이머신",18,DesktopTheme.FOREGROUND);title.setTypeface(ui.bold);title.setSingleLine(true);header.addView(title,ui.weight());
        Button add=ui.button("타이머 추가","add",false,false);add.setTextSize(11);add.setOnClickListener(v->perform(()->{commitEditors();TimerController.addTimer(this);refresh();}));header.addView(add);space(header,6);
        Button settings=ui.button("설정","settings",false,false);settings.setTextSize(11);settings.setOnClickListener(v->{commitEditors();showSettings();});header.addView(settings);root.addView(header);
        summary=ui.text("나의 타이머",12,DesktopTheme.MUTED);summary.setTypeface(ui.bold);summary.setPadding(dp(2),dp(12),0,dp(8));root.addView(summary);
        notice=ui.text("",12,DesktopTheme.WARM);notice.setPadding(0,0,0,dp(8));notice.setVisibility(View.GONE);root.addView(notice);
        ScrollView scroll=new ScrollView(this);cardList=ui.column();scroll.addView(cardList);root.addView(scroll,new LinearLayout.LayoutParams(-1,0,1));
        LinearLayout footer=ui.row();footer.setPadding(0,dp(12),0,0);
        all=ui.button("전체 시작","play",false,false);all.setOnClickListener(v->cycleAll());footer.addView(all,ui.weight());root.addView(footer);
        setContentView(root);
        if(Build.VERSION.SDK_INT>=33){backCallback=this::requestClose;getOnBackInvokedDispatcher().registerOnBackInvokedCallback(android.window.OnBackInvokedDispatcher.PRIORITY_DEFAULT,backCallback);}
        if(state!=null&&state.getBoolean("pickerOpen"))toast("파일 선택이 중단되었습니다. 파일 선택 버튼을 다시 누르세요.");
        refresh();
    }
    // The API 33+ branch supplies RECEIVER_NOT_EXPORTED; the legacy overload is guarded.
    @SuppressLint("UnspecifiedRegisterReceiverFlag")
    @Override protected void onResume(){
        super.onResume();resumed=true;perform(()->TimerController.reconcile(this));
        IntentFilter filter=new IntentFilter(TimerController.ACTION_STATE_CHANGED);
        if(Build.VERSION.SDK_INT>=33)registerReceiver(receiver,filter,Context.RECEIVER_NOT_EXPORTED);else registerReceiver(receiver,filter);
        receiverRegistered=true;handler.removeCallbacks(ticker);handler.post(ticker);
    }
    @Override protected void onPause(){commitEditors();if(activeAlarmEditor!=null)activeAlarmEditor.commitVolume();resumed=false;handler.removeCallbacks(ticker);if(receiverRegistered){unregisterReceiver(receiver);receiverRegistered=false;}super.onPause();}
    @Override public boolean dispatchTouchEvent(MotionEvent event){
        if(event.getAction()==MotionEvent.ACTION_DOWN){View focused=getCurrentFocus();if(focused instanceof EditText){boolean sameTimeField=false;for(Card card:cards.values()){if(card.duration.containsEditor(focused)){Rect groupBounds=new Rect();card.duration.getGlobalVisibleRect(groupBounds);sameTimeField=groupBounds.contains((int)event.getRawX(),(int)event.getRawY());break;}}Rect bounds=new Rect();focused.getGlobalVisibleRect(bounds);if(!sameTimeField&&!bounds.contains((int)event.getRawX(),(int)event.getRawY())){focused.clearFocus();commitEditors();}}}
        return super.dispatchTouchEvent(event);
    }
    @Override protected void onSaveInstanceState(Bundle state){state.putBoolean("pickerOpen",pickerCallback!=null);super.onSaveInstanceState(state);}
    // Only the API <=32 fallback; API 33+ uses the native callback registered in onCreate.
    @SuppressLint("GestureBackNavigation")
    @Override public void onBackPressed(){if(Build.VERSION.SDK_INT<=32)requestClose();else super.onBackPressed();}
    @Override protected void onDestroy(){if(Build.VERSION.SDK_INT>=33&&backCallback!=null)getOnBackInvokedDispatcher().unregisterOnBackInvokedCallback(backCallback);for(Popup popup:new ArrayList<>(openPopups))popup.dismiss();openPopups.clear();handler.removeCallbacks(ticker);mediaWork.shutdownNow();super.onDestroy();}

    private void refresh(){
        if(isFinishing()||isDestroyed()||cardList==null)return;
        try{
            snapshot=TimerController.read(this);List<String> ids=new ArrayList<>();for(TimerState timer:snapshot.timers)ids.add(timer.spec.id);
            if(!ids.equals(new ArrayList<>(cards.keySet()))){Map<String,Card> previous=new LinkedHashMap<>(cards);cards.clear();cardList.removeAllViews();for(TimerState timer:snapshot.timers){Card card=previous.get(timer.spec.id);if(card==null)card=new Card(timer);cards.put(timer.spec.id,card);LinearLayout.LayoutParams params=ui.fill();params.bottomMargin=dp(12);cardList.addView(card.view,params);}}
            int active=0;boolean stopped=false;
            for(TimerState timer:snapshot.timers){Card card=cards.get(timer.spec.id);if(card!=null)card.update(timer);if(TimerEngine.isActive(timer))active++;stopped|=timer.phase==TimerState.Phase.STOPPED||timer.phase==TimerState.Phase.COMPLETED;}
            summary.setText("나의 타이머  ·  "+snapshot.timers.size()+"개"+(active>0?" 중 "+active+"개 실행 중":""));
            all.setText(active>0?"전체 중단":stopped?"전체 재시작":"전체 시작");ui.style(all,active>0?"stop":stopped?"restart":"play",false,false,false);all.setEnabled(!snapshot.timers.isEmpty());
            notice.setText(snapshot.recoveryNotice);notice.setVisibility(snapshot.recoveryNotice.isEmpty()?View.GONE:View.VISIBLE);
        }catch(RuntimeException error){notice.setVisibility(View.VISIBLE);notice.setText(message(error));}
    }

    private final class Card{
        final LinearLayout view=ui.column(),nameSlot=ui.column(),audioArea=ui.column();
        final TextView name=ui.text("",14,DesktopTheme.FOREGROUND),status=ui.text("",12,DesktopTheme.MUTED),audioTime=ui.text("재생 대기",10,0xffffcf5f);
        final EditText nameInput=ui.input("","타이머 이름");
        final Button action=ui.button("시작","play",true,false);
        final DesktopDurationInput duration=new DesktopDurationInput(MainActivity.this);
        final HourglassView hourglass=new HourglassView(MainActivity.this);
        final DesktopProgressView progress=new DesktopProgressView(MainActivity.this),audioBar=new DesktopProgressView(MainActivity.this);
        final String defaultName;TimerState state;boolean editingName,committingName;
        Card(TimerState timer){
            state=timer;defaultName="타이머 "+(cards.size()+1);view.setPadding(dp(10),dp(10),dp(10),dp(10));android.graphics.drawable.GradientDrawable cardBackground=ui.shape(DesktopTheme.CARD,16);cardBackground.setStroke(dp(1),DesktopTheme.BORDER);view.setBackground(cardBackground);
            LinearLayout header=ui.row();name.setTypeface(ui.bold);name.setSingleLine(true);name.setEllipsize(TextUtils.TruncateAt.END);name.setMinHeight(dp(34));name.setGravity(Gravity.CENTER_VERTICAL);
            nameInput.setTextSize(14);nameInput.setTypeface(ui.bold);nameInput.setFilters(new InputFilter[]{new InputFilter.LengthFilter(80)});nameInput.setVisibility(View.GONE);nameInput.setImeOptions(EditorInfo.IME_ACTION_DONE);
            nameInput.setOnFocusChangeListener((v,focused)->{if(!focused)commitName();});nameInput.setOnEditorActionListener((v,id,event)->{if(id==EditorInfo.IME_ACTION_DONE||event!=null&&event.getKeyCode()==KeyEvent.KEYCODE_ENTER){commitName();root.requestFocus();return true;}return false;});
            name.setCompoundDrawablesWithIntrinsicBounds(null,null,new DesktopTheme.Glyph("edit",DesktopTheme.ACCENT,dp(18)),null);name.setCompoundDrawablePadding(dp(7));nameSlot.addView(name,new LinearLayout.LayoutParams(-2,-2));nameSlot.addView(nameInput,ui.fill());header.addView(nameSlot,ui.weight());
            name.setOnClickListener(v->{editingName=true;name.setVisibility(View.GONE);nameInput.setVisibility(View.VISIBLE);nameInput.setText(state.spec.name);nameInput.requestFocus();nameInput.selectAll();keyboard(nameInput);});
            Button alarm=ui.button("알람 설정","bell",false,true);alarm.setTextSize(10);alarm.setOnClickListener(v->{commitEditors();showAlarmSettings(state.spec);});header.addView(alarm);space(header,3);
            Button delete=ui.button("삭제","delete",false,true);delete.setTextSize(10);delete.setOnClickListener(v->{commitEditors();confirmDelete(state.spec);});header.addView(delete);view.addView(header);
            LinearLayout countdown=ui.row();countdown.setPadding(0,dp(5),0,dp(5));countdown.setMinimumHeight(dp(83));countdown.addView(hourglass,new LinearLayout.LayoutParams(dp(72),dp(78)));
            duration.setValue(timer.spec.durationSeconds);duration.onCommit(seconds->perform(()->{TimerState latest=find(timer.spec.id);if(latest==null||TimerEngine.isActive(latest))return;TimerSpec next=latest.spec.copy();next.durationSeconds=seconds;TimerController.updateTimer(MainActivity.this,next);refresh();}));
            countdown.addView(duration,new LinearLayout.LayoutParams(dp(132),-2));status.setGravity(Gravity.CENTER);status.setMaxLines(2);LinearLayout.LayoutParams sp=new LinearLayout.LayoutParams(0,-2,1);sp.leftMargin=dp(6);sp.rightMargin=dp(6);countdown.addView(status,sp);
            action.setTextSize(12);action.setOnClickListener(v->cycle(state.spec.id));countdown.addView(action,new LinearLayout.LayoutParams(dp(76),dp(42)));countdown.addOnLayoutChangeListener((v,left,top,right,bottom,oldLeft,oldTop,oldRight,oldBottom)->{int available=right-left-countdown.getPaddingLeft()-countdown.getPaddingRight()-(hourglass.getVisibility()==View.VISIBLE?dp(72):0)-dp(76+52);int width=Math.min(dp(snapshot.useHourglass?132:165),Math.max(dp(80),available));LinearLayout.LayoutParams params=(LinearLayout.LayoutParams)duration.getLayoutParams();if(params.width!=width){params.width=width;duration.setLayoutParams(params);}});view.addView(countdown);
            LinearLayout labels=ui.row();labels.addView(ui.text("오디오 남은 시간",10,DesktopTheme.MUTED),ui.weight());labels.addView(audioTime);audioArea.addView(labels);
            LinearLayout.LayoutParams ap=new LinearLayout.LayoutParams(-1,dp(3));ap.topMargin=dp(2);audioArea.addView(audioBar,ap);audioArea.setPadding(0,0,0,dp(5));view.addView(audioArea);view.addView(progress,new LinearLayout.LayoutParams(-1,dp(4)));update(timer);
        }
        void commitName(){
            if(!editingName||committingName)return;committingName=true;
            try{String value=nameInput.getText().toString().trim();if(value.isEmpty())value=defaultName;TimerState latest=find(state.spec.id);if(latest!=null&&!latest.spec.name.equals(value)){TimerSpec next=latest.spec.copy();next.name=value;TimerController.updateTimer(MainActivity.this,next);}editingName=false;nameInput.setVisibility(View.GONE);name.setVisibility(View.VISIBLE);refresh();}
            catch(RuntimeException error){toast(message(error));}finally{committingName=false;}
        }
        void update(TimerState timer){
            state=timer;if(!editingName)name.setText(timer.spec.name);
            boolean active=TimerEngine.isActive(timer);duration.setReadOnly(active);duration.setCompact(snapshot.useHourglass);duration.setValue(timer.spec.durationSeconds);int remaining=TimerEngine.remainingSeconds(timer,SystemClock.elapsedRealtime());duration.display(remaining);hourglass.setVisibility(snapshot.useHourglass?View.VISIBLE:View.GONE);hourglass.setState(timer);
            double exactRemaining=TimerEngine.remainingMilliseconds(timer,SystemClock.elapsedRealtime())/1000d;
            double fraction=timer.phase==TimerState.Phase.IDLE?(timer.spec.durationSeconds>0?1:0):timer.spec.durationSeconds>0?exactRemaining/timer.spec.durationSeconds:0;progress.update(fraction,timer.phase==TimerState.Phase.ALARMING?DesktopTheme.WARM:timer.phase==TimerState.Phase.STOPPED||timer.phase==TimerState.Phase.COMPLETED?DesktopTheme.MUTED:DesktopTheme.ACCENT,false);
            String label="";if(timer.phase==TimerState.Phase.RUNNING)label="실행 중";else if(timer.phase==TimerState.Phase.STOPPED)label="중단";else if(timer.phase==TimerState.Phase.COMPLETED)label="완료";
            PlaybackService.AudioProgress audio=PlaybackService.audioProgress(MainActivity.this,timer.spec.id);
            if(TimerEngine.isAudioActive(timer)){if("preparing".equals(audio.status))label="알람 준비 중";else if("fallback".equals(audio.status))label="기본 멜로디 재생";else if("deviceError".equals(audio.status))label="오디오 장치 확인";else label=timer.spec.reverse?"역알람 재생 중":"알람 재생 중";}
            if(!timer.playbackError.isEmpty())label=timer.playbackError;status.setText(label);status.setTooltipText(label);status.setVisibility(label.isEmpty()?View.INVISIBLE:View.VISIBLE);status.setTextColor(timer.phase==TimerState.Phase.ALARMING?DesktopTheme.WARM:timer.phase==TimerState.Phase.RUNNING?DesktopTheme.ACCENT:DesktopTheme.MUTED);
            boolean stopped=timer.phase==TimerState.Phase.STOPPED||timer.phase==TimerState.Phase.COMPLETED;action.setText(active?"중단":stopped?"재시작":"시작");ui.style(action,active?"stop":stopped?"restart":"play",!active||timer.phase==TimerState.Phase.ALARMING,false,timer.phase==TimerState.Phase.ALARMING);
            audioArea.setVisibility(timer.spec.reverse?View.VISIBLE:View.GONE);audioTime.setText(audio.durationMs>0?format(audio.remainingSeconds()):"preparing".equals(audio.status)?"확인 중":"deviceError".equals(audio.status)?"오디오 확인 필요":"재생 대기");audioBar.update(audio.fraction(),0xffffcf5f,true);
        }
    }
    private void commitEditors(){for(Card card:new ArrayList<>(cards.values())){card.duration.commit();card.commitName();}}
    private TimerState find(String id){return TimerController.find(TimerController.read(this),id);}
    private void cycle(String id){commitEditors();perform(()->{TimerState timer=find(id);if(timer==null)return;if(TimerEngine.isActive(timer)){TimerController.stop(this,id);refresh();}else startWithPermissions(()->perform(()->{TimerController.start(this,id);refresh();}));});}
    private void cycleAll(){commitEditors();perform(()->{boolean active=false;for(TimerState timer:TimerController.read(this).timers)active|=TimerEngine.isActive(timer);if(active){TimerController.stopAll(this);refresh();}else startWithPermissions(()->perform(()->{TimerController.all(this);refresh();}));});}
    private void startWithPermissions(Runnable action){
        if(!TimerController.canScheduleExact(this)){Popup p=new Popup("Android 설정","알람 및 리마인더","화면이 꺼져 있어도 정해진 시각에 타이머가 동작하도록 허용하세요.");p.save.setText("설정 열기");p.save.setOnClickListener(v->{p.dismiss();perform(()->startActivity(new Intent(Settings.ACTION_REQUEST_SCHEDULE_EXACT_ALARM,Uri.parse("package:"+getPackageName()))));});p.show();return;}
        if(Build.VERSION.SDK_INT>=33&&checkSelfPermission(Manifest.permission.POST_NOTIFICATIONS)!=PackageManager.PERMISSION_GRANTED){pendingStart=action;requestPermissions(new String[]{Manifest.permission.POST_NOTIFICATIONS},NOTIFICATIONS);return;}action.run();
    }
    @Override public void onRequestPermissionsResult(int request,String[] permissions,int[] results){super.onRequestPermissionsResult(request,permissions,results);if(request==NOTIFICATIONS){if(results.length==0||results[0]!=PackageManager.PERMISSION_GRANTED)toast("알림이 꺼져 있습니다. 알림에서 제어하려면 앱 알림을 허용하세요.");Runnable next=pendingStart;pendingStart=null;if(next!=null)next.run();}}
    private void confirmDelete(TimerSpec spec){Popup p=new Popup("삭제 확인","타이머를 삭제할까요?",spec.name);p.add(description("재생 중인 알람이 중단되고, 이 타이머의 설정이 삭제됩니다."));p.save.setText("삭제");ui.style(p.save,"delete",true,false,false);p.save.setOnClickListener(v->p.perform(()->{TimerController.removeTimer(this,spec.id);p.dismiss();refresh();}));p.show();}
    private void showSettings(){
        Snapshot before=AppStore.load(this);Popup p=new Popup("앱 설정","설정","타이머 표시와 화면을 닫는 방식을 선택하세요.");
        LinearLayout display=section(null);CheckBox hourglass=ui.check("모래시계 표시",before.useHourglass);display.addView(hourglass);display.addView(description("시작할 때 모래시계가 뒤집히고, 시간에 맞춰 모래가 흐릅니다.\n모든 타이머에 적용되며 남은 시간도 함께 표시됩니다."));p.add(display);
        LinearLayout background=section(null);CheckBox minimize=ui.check("화면을 닫으면 백그라운드 재생",Boolean.TRUE.equals(before.minimizeToBackground));background.addView(minimize);background.addView(description("뒤로 가기로 화면을 닫으면 백그라운드로 보냅니다.\n타이머와 알람은 계속 동작합니다."));p.add(background);
        LinearLayout external=section(null);CheckBox confirm=ui.check("다운로드 및 개인 알람 이용 권한을 확인했습니다.",before.externalLinksConfirmed);external.addView(confirm);TextView usage=link("외부 링크 이용 안내");usage.setOnClickListener(v->showText("외부 링크 이용 안내",asset("licenses/ExternalLinkUsage.ko.txt")));external.addView(usage);p.add(external);
        TextView licenses=link("글꼴 · 오디오 엔진 라이선스");licenses.setPadding(0,dp(4),0,dp(10));licenses.setOnClickListener(v->showText("오픈소스 라이선스",licenseText()));p.add(licenses);
        p.save.setOnClickListener(v->p.perform(()->{Snapshot latest=AppStore.load(this);latest.useHourglass=hourglass.isChecked();latest.minimizeToBackground=minimize.isChecked();latest.externalLinksConfirmed=confirm.isChecked();AppStore.save(this,latest);p.dismiss();refresh();}));p.show();
    }
    private void requestClose(){
        if(closing)return;commitEditors();Snapshot state=AppStore.load(this);if(state.closeBehaviorConfirmed){closeWithChoice(Boolean.TRUE.equals(state.minimizeToBackground),false);return;}
        Popup p=new Popup("처음 한 번만 선택","백그라운드로 보낼까요?","화면을 닫아도 타이머와 알람이 계속 동작합니다.");p.add(description("선택한 내용은 저장됩니다.\n설정 창에서 언제든 백그라운드 재생 여부를 변경할 수 있습니다."));p.save.setText("백그라운드로 보내기");ui.style(p.save,null,true,false,false);
        Button exit=ui.button("완전 종료",null,false,false);p.footer.addView(exit,1);exit.setOnClickListener(v->p.perform(()->{p.dismiss();closeWithChoice(false,true);}));p.save.setOnClickListener(v->p.perform(()->{p.dismiss();closeWithChoice(true,true);}));p.show();
    }
    private void closeWithChoice(boolean background,boolean remember){perform(()->{if(remember){Snapshot latest=AppStore.load(this);latest.minimizeToBackground=background;latest.closeBehaviorConfirmed=true;AppStore.save(this,latest);}if(background)moveTaskToBack(true);else{TimerController.shutdown(this);closing=true;finishAndRemoveTask();}});}
    private void showAlarmSettings(TimerSpec original){activeAlarmEditor=new AlarmEditor(original);activeAlarmEditor.show();}
    private final class AlarmEditor {
        final TimerSpec draft;
        final Popup p;
        final LinearLayout modeSection,soundSection,volumeSection,pathRow,linkRow,playlistRow;
        final RadioGroup modes=new RadioGroup(MainActivity.this),sources=new DesktopRadioGroup(MainActivity.this);
        final RadioButton normal=ui.radio("일반 알람"),reverse=ui.radio("역알람"),builtin=ui.radio("내장 멜로디"),local=ui.radio("내 파일"),external=ui.radio("외부 링크"),playlist=ui.radio("재생 목록");
        final EditText filePath,linkPath,volumeNumber;
        final SeekBar volume=new SeekBar(MainActivity.this);
        final TextView modeDescription=description(""),playlistSummary=description(""),permissionWarning=errorText(),linkHelp;
        final DesktopDownloadView download=new DesktopDownloadView(MainActivity.this);
        final ExternalMedia.CancellationToken cancellation=new ExternalMedia.CancellationToken();
        boolean syncingVolume,committingVolume,preparing;int savedVolume;
        AlarmEditor(TimerSpec original){
            draft=original.copy();savedVolume=original.volume;p=new Popup("타이머별 설정","알람 설정",original.name);
            modeSection=section("알람 방식");modes.setOrientation(LinearLayout.HORIZONTAL);modes.addView(normal);modes.addView(reverse);(draft.reverse?reverse:normal).setChecked(true);modeSection.addView(modes);modeSection.addView(modeDescription);
            modes.setOnCheckedChangeListener((group,id)->{updateMode();commitVolume();});updateMode();p.add(modeSection);
            soundSection=section("알람음");sources.setOrientation(LinearLayout.HORIZONTAL);
            for(RadioButton option:new RadioButton[]{builtin,local,external,playlist}){option.setTextSize(12);option.setPadding(0,0,dp(10),0);sources.addView(option,new RadioGroup.LayoutParams(-2,-2));}
            boolean savedExternal=ExternalMedia.isLink(draft.soundUri);(draft.usePlaylist?playlist:savedExternal?external:draft.soundUri.isEmpty()?builtin:local).setChecked(true);soundSection.addView(sources);
            filePath=ui.input(savedExternal?"":draft.soundUri,"알람으로 사용할 파일 경로");linkPath=ui.input(savedExternal?draft.soundUri:"","이용 권한이 있는 영상·오디오의 https:// 링크");
            filePath.setContentDescription("알람 파일 경로");linkPath.setContentDescription("알람으로 사용할 외부 링크");linkPath.setInputType(InputType.TYPE_CLASS_TEXT|InputType.TYPE_TEXT_VARIATION_URI);
            pathRow=ui.row();pathRow.addView(filePath,ui.weight());space(pathRow,8);
            Button browse=ui.button("파일 선택","folder",false,false);browse.setTextSize(11);browse.setOnClickListener(v->pickFiles(p,false,tracks->{if(tracks.isEmpty())return;Track item=tracks.get(0);filePath.setText(item.uri);draft.soundTitle=item.title;local.setChecked(true);}));pathRow.addView(browse);soundSection.addView(pathRow);
            linkRow=ui.row();linkRow.addView(linkPath,ui.weight());space(linkRow,8);Button location=ui.button("","folder",false,false);location.setContentDescription("다운로드된 파일 위치 열기");location.setOnClickListener(v->showFileLocation(linkPath.getText().toString().trim(),p));linkRow.addView(location,new LinearLayout.LayoutParams(dp(42),dp(40)));soundSection.addView(linkRow);
            linkHelp=description("유튜브 등 공개된 단일 영상·오디오 링크를 입력하세요.\n해당 링크의 오디오를 기기에 다운로드하며, 다운로드가 완료되면 자동으로 해당 파일이 알람으로 설정됩니다.");soundSection.addView(linkHelp);
            download.setVisibility(View.GONE);soundSection.addView(download);
            playlistRow=ui.row();Button list=ui.button("재생 목록 설정","settings",false,false);list.setTextSize(11);list.setOnClickListener(v->new PlaylistEditor(draft,this::updatePlaylistSummary).show());playlistRow.addView(list);space(playlistRow,10);playlistRow.addView(playlistSummary,ui.weight());soundSection.addView(playlistRow);updatePlaylistSummary();
            permissionWarning.setText("앱 설정에서 외부 링크 안내를 확인하고 다운로드 및 개인 알람 이용 권한을 확인해주세요.");permissionWarning.setVisibility(View.GONE);soundSection.addView(permissionWarning);
            sources.setOnCheckedChangeListener((group,id)->{updateSource();commitVolume();});updateSource();p.add(soundSection);
            volumeSection=section("음량");LinearLayout volumeRow=ui.row();volume.setMax(100);volume.setProgress(draft.volume);volume.setProgressTintList(android.content.res.ColorStateList.valueOf(DesktopTheme.ACCENT));volume.setThumbTintList(android.content.res.ColorStateList.valueOf(DesktopTheme.ACCENT));volumeRow.addView(volume,ui.weight());space(volumeRow,10);
            volumeNumber=ui.input(String.valueOf(draft.volume),"");volumeNumber.setInputType(InputType.TYPE_CLASS_NUMBER);volumeNumber.setHorizontallyScrolling(false);volumeNumber.setGravity(Gravity.CENTER);volumeNumber.setSelectAllOnFocus(true);volumeNumber.setImeOptions(EditorInfo.IME_ACTION_DONE);
            LinearLayout numberField=ui.row(),steppers=ui.column();numberField.addView(volumeNumber,new LinearLayout.LayoutParams(dp(49),dp(42)));
            for(int direction:new int[]{1,-1}){Button step=ui.button(direction>0?"▴":"▾",null,false,true);step.setContentDescription(direction>0?"음량 높이기":"음량 낮추기");step.setTextSize(10);step.setMinHeight(0);step.setMinimumHeight(0);step.setPadding(0,0,0,0);steppers.addView(step,new LinearLayout.LayoutParams(-1,0,1));step.setOnClickListener(v->stepVolume(direction));}
            numberField.addView(steppers,new LinearLayout.LayoutParams(dp(18),dp(42)));volumeRow.addView(numberField);volumeSection.addView(volumeRow);volumeSection.addView(description("0 무음  ·  50 절반 음량  ·  100 원본 음량"));p.add(volumeSection);
            volume.setOnSeekBarChangeListener(new SeekBar.OnSeekBarChangeListener(){
                public void onProgressChanged(SeekBar bar,int progress,boolean user){if(!syncingVolume){syncingVolume=true;volumeNumber.setText(String.valueOf(progress));syncingVolume=false;}}
                public void onStartTrackingTouch(SeekBar bar){}public void onStopTrackingTouch(SeekBar bar){commitVolume();}
            });
            volumeNumber.addTextChangedListener(watcher(()->{if(syncingVolume)return;try{int number=Integer.parseInt(volumeNumber.getText().toString());if(number>=0&&number<=100){syncingVolume=true;volume.setProgress(number);syncingVolume=false;}}catch(NumberFormatException ignored){}}));
            volumeNumber.setOnFocusChangeListener((v,focused)->{if(!focused)commitVolume();});
            volumeNumber.setOnEditorActionListener((v,id,event)->{if(id==EditorInfo.IME_ACTION_DONE||event!=null&&event.getKeyCode()==KeyEvent.KEYCODE_ENTER){commitVolume();volumeNumber.clearFocus();hideKeyboard(volumeNumber);return true;}return false;});
            volumeNumber.setOnKeyListener((v,key,event)->{if(event.getAction()==KeyEvent.ACTION_DOWN&&(key==KeyEvent.KEYCODE_DPAD_UP||key==KeyEvent.KEYCODE_DPAD_DOWN)){stepVolume(key==KeyEvent.KEYCODE_DPAD_UP?1:-1);return true;}return false;});
            p.cancelTasks.add(cancellation);p.save.setOnClickListener(v->save());p.beforeCancel=this::commitVolume;p.commitField=volumeNumber;p.beforeOutside=this::commitVolume;p.afterDismiss=()->{if(activeAlarmEditor==this)activeAlarmEditor=null;};
        }
        void show(){p.show();}
        void updateMode(){modeDescription.setText(reverse.isChecked()?"역알람: 타이머 실행 중 재생하고, 중단하거나 0이 되면 일시정지합니다.\n다시 시작하면 중단한 위치부터 이어 재생합니다.":"일반 알람: 남은 시간이 0이 되면 알람이 울립니다.");}
        void stepVolume(int direction){volumeNumber.setText(String.valueOf(Math.max(0,Math.min(100,boundedInput(volumeNumber,100,volume.getProgress())+direction))));commitVolume();}
        void updatePlaylistSummary(){playlistSummary.setText(draft.playlist.isEmpty()?"등록된 파일 없음":"파일 "+draft.playlist.size()+"개 · "+(draft.shuffle?"무작위 재생":"순차 재생"));}
        void updateSource(){filePath.setEnabled(local.isChecked());pathRow.setVisibility(!external.isChecked()&&!playlist.isChecked()?View.VISIBLE:View.GONE);linkRow.setVisibility(external.isChecked()?View.VISIBLE:View.GONE);linkHelp.setVisibility(external.isChecked()?View.VISIBLE:View.GONE);playlistRow.setVisibility(playlist.isChecked()?View.VISIBLE:View.GONE);permissionWarning.setVisibility(View.GONE);if(!external.isChecked())download.setVisibility(View.GONE);}
        boolean commitVolume(){
            if(committingVolume||preparing||!p.alive())return true;committingVolume=true;
            try{int next=boundedInput(volumeNumber,100,volume.getProgress());syncingVolume=true;volumeNumber.setText(String.valueOf(next));volume.setProgress(next);syncingVolume=false;if(next!=savedVolume){TimerController.updateVolume(MainActivity.this,draft.id,next);savedVolume=next;draft.volume=next;refresh();}p.error.setText("");return true;}
            catch(RuntimeException error){p.error.setText(message(error));return false;}finally{committingVolume=false;}
        }
        void setBusy(boolean busy){preparing=busy;enableTree(modeSection,!busy);enableTree(soundSection,!busy);enableTree(volumeSection,!busy);p.save.setEnabled(!busy);}
        void save(){
            if(preparing||!commitVolume())return;p.error.setText("");permissionWarning.setVisibility(View.GONE);
            draft.reverse=reverse.isChecked();draft.volume=savedVolume;draft.usePlaylist=playlist.isChecked();draft.soundUri=builtin.isChecked()||draft.usePlaylist?"":external.isChecked()?linkPath.getText().toString().trim():filePath.getText().toString().trim();
            List<String> links=new ArrayList<>();
            if(draft.usePlaylist){
                if(draft.playlist.isEmpty()){p.error.setText("재생 목록 설정에서 사용할 파일이나 외부 링크를 등록하고 확인하세요.");return;}
                for(Track track:draft.playlist){if(track.remote){if(!ExternalMedia.isLink(track.uri)){p.error.setText("재생 목록 설정에서 사용할 파일이나 외부 링크를 등록하고 확인하세요.");return;}if(!links.contains(track.uri))links.add(track.uri);}else if(!localAvailable(track.uri)){p.error.setText("재생 목록 설정에서 사용할 파일이나 외부 링크를 등록하고 확인하세요.");return;}}
            }else if(external.isChecked()){
                if(!ExternalMedia.isLink(draft.soundUri)){p.error.setText("http 또는 https로 시작하는 영상·오디오 링크를 입력하세요.");return;}links.add(draft.soundUri);
            }else if(local.isChecked()&&!localAvailable(draft.soundUri)){p.error.setText("알람으로 사용할 파일을 선택하거나 내장 멜로디를 선택하세요.");return;}
            if(!links.isEmpty()&&!AppStore.load(MainActivity.this).externalLinksConfirmed){permissionWarning.setVisibility(View.VISIBLE);return;}
            if(links.isEmpty()){apply();return;}
            setBusy(true);download.reset();p.error.setTextColor(DesktopTheme.MUTED);p.error.setText("링크 오디오를 준비하고 있습니다. 취소하면 중단됩니다.");
            mediaWork.execute(()->{
                try{for(String url:links)ExternalMedia.prepare(MainActivity.this,url,cancellation,value->handler.post(()->{if(p.alive()&&preparing)download.update(value);}));handler.post(()->{if(p.alive()){setBusy(false);p.error.setTextColor(DesktopTheme.ERROR);apply();}});}
                catch(Exception error){handler.post(()->{if(p.alive()){setBusy(false);p.error.setTextColor(DesktopTheme.ERROR);p.error.setText(message(error));download.failure();}});}
            });
        }
        void apply(){p.perform(()->{TimerState latest=find(draft.id);if(latest==null){p.dismiss();return;}draft.name=latest.spec.name;draft.durationSeconds=latest.spec.durationSeconds;TimerController.updateTimer(MainActivity.this,draft);p.dismiss();refresh();});}
    }
    private final class PlaylistEditor {
        final TimerSpec outer;
        final Popup p=new Popup("","재생 목록","");
        final LinearLayout tools=ui.row(),rowsView=ui.column();
        final TextView presetCaption=ui.text("",12,DesktopTheme.MUTED),help=description("파일 또는 외부 링크를 등록하세요. 마지막 항목 뒤에는 목록을 반복합니다."),empty=description("파일 추가 버튼을 눌러 알람 파일을 등록하세요.");
        final CheckBox shuffle;
        final List<PlaylistRow> rows=new ArrayList<>();
        final ExternalMedia.CancellationToken cancellation=new ExternalMedia.CancellationToken();
        final Runnable applied;
        String selectedPreset;boolean preparing;
        PlaylistEditor(TimerSpec outer,Runnable applied){
            this.outer=outer;this.applied=applied;selectedPreset=outer.presetId==null?PresetLibrary.DEFAULT_ID:outer.presetId;
            p.add(presetCaption);tools.setGravity(Gravity.RIGHT|Gravity.CENTER_VERTICAL);
            Button presets=ui.button("프리셋","folder",false,false);presets.setOnClickListener(v->showPresets());tools.addView(presets);space(tools,8);
            Button add=ui.button("파일 추가","add",false,false);add.setOnClickListener(v->{PlaylistRow row=addRow(new Track());row.path.requestFocus();p.scroll.post(()->p.scroll.smoothScrollTo(0,row.view.getBottom()));});tools.addView(add);p.add(tools);
            shuffle=ui.check("무작위 재생",outer.shuffle);LinearLayout shuffleRow=ui.row();shuffleRow.setGravity(Gravity.RIGHT|Gravity.CENTER_VERTICAL);shuffleRow.addView(shuffle);p.add(shuffleRow);p.add(help);
            rowsView.addView(empty);p.add(rowsView);for(Track track:outer.playlist)addRow(track.copy());
            updatePresetCaption();p.cancelTasks.add(cancellation);p.save.setOnClickListener(v->save());
        }
        void show(){p.show();}
        void updatePresetCaption(){
            PlaylistPreset preset=PresetLibrary.find(AppStore.load(MainActivity.this).presets,selectedPreset);
            presetCaption.setText(preset==null?"프리셋: 선택 안 함":"프리셋: "+preset.name);
            if(!selectedPreset.isEmpty()&&preset==null)p.error.setText("이전에 선택한 프리셋이 없습니다. 프리셋을 다시 선택하세요.");
        }
        PlaylistRow addRow(Track track){PlaylistRow row=new PlaylistRow(track);rows.add(row);LinearLayout.LayoutParams params=ui.fill();params.topMargin=dp(8);rowsView.addView(row.view,params);renumber();return row;}
        void renumber(){empty.setVisibility(rows.isEmpty()?View.VISIBLE:View.GONE);for(int i=0;i<rows.size();i++){rows.get(i).number=i+1;rows.get(i).updateTitle();}}
        void showPresets(){
            for(PlaylistRow row:rows)row.commitPreview();Snapshot stored=AppStore.load(MainActivity.this);
            Popup presetsPopup=new Popup("재생 목록","프리셋","프리셋을 만들거나 불러온 뒤, 재생 목록 창에서 저장하세요.");
            List<String> labels=new ArrayList<>();int selected=-1;
            for(int i=0;i<stored.presets.size();i++){PlaylistPreset preset=stored.presets.get(i);labels.add(preset.name+"  ·  파일 "+preset.items.size()+"개");if(preset.id.equals(selectedPreset))selected=i;}
            if(selected<0)for(int i=0;i<stored.presets.size();i++)if(PresetLibrary.DEFAULT_ID.equals(stored.presets.get(i).id))selected=i;
            final int[] selection={selected};ListView list=new ListView(MainActivity.this);list.setChoiceMode(ListView.CHOICE_MODE_SINGLE);list.setBackground(ui.shape(DesktopTheme.ELEVATED,5));
            list.setAdapter(new ArrayAdapter<String>(MainActivity.this,android.R.layout.simple_list_item_single_choice,labels){@Override public View getView(int position,View convert,ViewGroup parent){TextView view=(TextView)super.getView(position,convert,parent);view.setTextColor(DesktopTheme.FOREGROUND);view.setTypeface(ui.regular);view.setTextSize(13);view.setMinHeight(dp(40));return view;}});
            if(selected>=0)list.setItemChecked(selected,true);list.setOnItemClickListener((parent,view,position,id)->selection[0]=position);
            presetsPopup.add(list,new LinearLayout.LayoutParams(-1,dp(200)));
            LinearLayout createRow=ui.row();EditText name=ui.input("","새 프리셋 이름");name.setFilters(new InputFilter[]{new InputFilter.LengthFilter(80)});createRow.addView(name,ui.weight());space(createRow,10);
            Button create=ui.button("생성","add",false,false);createRow.addView(create);presetsPopup.add(createRow);
            create.setOnClickListener(v->presetsPopup.perform(()->{Snapshot latest=AppStore.load(MainActivity.this);PlaylistPreset made=PresetLibrary.create(latest.presets,name.getText().toString());AppStore.save(MainActivity.this,latest);selectedPreset=made.id;updatePresetCaption();presetsPopup.dismiss();}));
            name.setImeOptions(EditorInfo.IME_ACTION_DONE);name.setOnEditorActionListener((v,id,event)->{if(id==EditorInfo.IME_ACTION_DONE||event!=null&&event.getKeyCode()==KeyEvent.KEYCODE_ENTER){create.performClick();return true;}return false;});
            presetsPopup.save.setText("불러오기");ui.style(presetsPopup.save,"folder",true,false,false);presetsPopup.save.setEnabled(!stored.presets.isEmpty());
            presetsPopup.save.setOnClickListener(v->presetsPopup.perform(()->{if(selection[0]<0)return;PlaylistPreset chosen=PresetLibrary.find(AppStore.load(MainActivity.this).presets,stored.presets.get(selection[0]).id);if(chosen==null)throw new IllegalArgumentException("선택한 프리셋을 찾을 수 없습니다. 프리셋을 다시 선택하세요.");selectedPreset=chosen.id;for(PlaylistRow row:rows)row.cancelPreview();rows.clear();rowsView.removeAllViews();rowsView.addView(empty);for(Track track:chosen.items)addRow(track.copy());shuffle.setChecked(chosen.shuffle);renumber();updatePresetCaption();presetsPopup.dismiss();}));
            presetsPopup.show();
        }
        void setBusy(boolean value){preparing=value;enableTree(tools,!value);enableTree(rowsView,!value);shuffle.setEnabled(!value);p.save.setEnabled(!value);}
        void save(){
            if(preparing)return;p.error.setText("");if(rows.isEmpty()){p.error.setText("알람으로 사용할 파일을 한 개 이상 등록하세요.");return;}
            List<Track> items=new ArrayList<>();
            for(int i=0;i<rows.size();i++){Track item=rows.get(i).read();if(item.remote&&!AppStore.load(MainActivity.this).externalLinksConfirmed){p.error.setText("앱 설정에서 외부 링크 안내를 확인하고 다운로드 및 개인 알람 이용 권한을 확인해주세요.");return;}
                if(item.remote?!ExternalMedia.isLink(item.uri):!localAvailable(item.uri)){p.error.setText(item.remote?"파일 "+(i+1)+"에 http 또는 https로 시작하는 영상·오디오 링크를 입력하세요.":"파일 "+(i+1)+"의 경로를 확인하고 실제 파일을 선택하세요.");rows.get(i).path.requestFocus();return;}items.add(item);
            }
            setBusy(true);mediaWork.execute(()->{
                try{
                    for(int i=0;i<items.size();i++){
                        Track item=items.get(i);PlaylistRow row=rows.get(i);final int number=i+1;
                        if(item.remote){
                            handler.post(()->{if(p.alive()){row.download.reset();p.error.setTextColor(DesktopTheme.MUTED);p.error.setText("파일 "+number+"의 오디오를 준비하고 있습니다. 취소하면 중단됩니다.");}});
                            ExternalMedia.Prepared prepared=ExternalMedia.prepare(MainActivity.this,item.uri,cancellation,value->handler.post(()->{if(p.alive()&&preparing)row.download.update(value);}));if(prepared.title!=null&&!prepared.title.isEmpty())item.title=prepared.title;
                        }
                        try{ExternalMedia.Preview preview=item.remote?ExternalMedia.preview(MainActivity.this,item.uri,cancellation):ExternalMedia.localPreview(MainActivity.this,item.uri,cancellation);if(preview.title!=null&&!preview.title.isEmpty())item.title=preview.title;item.thumbnailPath=preview.thumbnailPath;}
                        catch(Exception ignored){/* A missing thumbnail never blocks saving. */}
                        handler.post(()->{if(p.alive())row.applyMetadata(item.uri,item.title,item.thumbnailPath);});
                    }
                    handler.post(()->{if(!p.alive())return;p.perform(()->{if(!selectedPreset.isEmpty()){Snapshot latest=AppStore.load(MainActivity.this);PresetLibrary.savePlaylist(latest.presets,selectedPreset,items,shuffle.isChecked());AppStore.save(MainActivity.this,latest);}outer.playlist=copyTracks(items);outer.shuffle=shuffle.isChecked();outer.presetId=selectedPreset;applied.run();p.dismiss();});});
                }catch(Exception error){handler.post(()->{if(p.alive()){setBusy(false);p.error.setTextColor(DesktopTheme.ERROR);p.error.setText(message(error));}});}
            });
        }

        final class PlaylistRow {
            final LinearLayout view=section(null);
            final TextView title=ui.text("",13,DesktopTheme.FOREGROUND);
            final DesktopThumbnailView thumbnail=new DesktopThumbnailView(MainActivity.this);
            final EditText path=ui.input("","알람으로 사용할 파일 경로");
            final RadioGroup source=new RadioGroup(MainActivity.this);
            final RadioButton local=ui.radio("내 파일"),external=ui.radio("외부 링크");
            final Button browse=ui.button("파일 선택","folder",false,false);
            final DesktopDownloadView download=new DesktopDownloadView(MainActivity.this);
            final Runnable previewDelayed=this::preview;
            ExternalMedia.CancellationToken previewToken;
            String fileDraft="",linkDraft="",resolvedTitle="",resolvedFor="",thumbnailFor="",thumbnailPath="",localTitle="",localTitleFor="";
            boolean remote,changing,loading;int number;
            PlaylistRow(Track item){
                remote=item.remote||ExternalMedia.isLink(item.uri);if(remote)linkDraft=item.uri;else{fileDraft=item.uri;localTitle=item.title;localTitleFor=item.uri;}
                resolvedTitle=item.title;resolvedFor=item.uri;thumbnailPath=item.thumbnailPath;thumbnailFor=item.uri;
                LinearLayout main=ui.row();main.setGravity(Gravity.TOP);main.addView(thumbnail,new LinearLayout.LayoutParams(dp(64),dp(64)));space(main,10);
                LinearLayout editing=ui.column();title.setTypeface(ui.bold);title.setMaxLines(2);editing.addView(title);
                LinearLayout controls=ui.row();source.setOrientation(LinearLayout.HORIZONTAL);local.setTextSize(11);external.setTextSize(11);source.addView(local);source.addView(external);(remote?external:local).setChecked(true);controls.addView(source,ui.weight());
                Button delete=ui.button("삭제","delete",false,false);delete.setTextSize(10);delete.setOnClickListener(v->{cancelPreview();rows.remove(this);rowsView.removeView(view);renumber();});controls.addView(delete);editing.addView(controls);
                LinearLayout inputRow=ui.row();path.setText(item.uri);path.setImeOptions(EditorInfo.IME_ACTION_DONE);inputRow.addView(path,ui.weight());space(inputRow,7);browse.setTextSize(10);inputRow.addView(browse);editing.addView(inputRow);main.addView(editing,ui.weight());view.addView(main);
                download.setVisibility(View.GONE);view.addView(download);showThumbnail(thumbnailPath);
                source.setOnCheckedChangeListener((group,id)->switchSource());path.addTextChangedListener(watcher(()->{if(changing)return;cancelPreview();resolvedTitle="";resolvedFor="";thumbnailPath="";thumbnailFor="";thumbnail.setImageDrawable(null);updateTitle();handler.postDelayed(previewDelayed,700);}));
                path.setOnFocusChangeListener((v,focus)->{if(!focus)commitPreview();});path.setOnEditorActionListener((v,id,event)->{if(id==EditorInfo.IME_ACTION_DONE||event!=null&&event.getKeyCode()==KeyEvent.KEYCODE_ENTER){commitPreview();path.clearFocus();hideKeyboard(path);return true;}return false;});
                browse.setOnClickListener(v->{if(remote)showFileLocation(path.getText().toString().trim(),p);else pickFiles(p,false,tracks->{if(!tracks.isEmpty()){localTitle=tracks.get(0).title;localTitleFor=tracks.get(0).uri;path.setText(tracks.get(0).uri);commitPreview();}});});updateMode();handler.post(previewDelayed);
            }
            void updateMode(){path.setHint(remote?"영상·오디오의 https:// 링크 · 저장 시 다운로드":"알람으로 사용할 파일 경로");path.setContentDescription(remote?"재생 목록 외부 링크":"재생 목록 파일 경로");browse.setText(remote?"파일 위치":"파일 선택");browse.setContentDescription(remote?"다운로드된 파일 위치 열기":"알람 파일 선택");download.setVisibility(View.GONE);updateTitle();}
            void switchSource(){boolean next=external.isChecked();if(next!=remote){cancelPreview();if(remote)linkDraft=path.getText().toString();else fileDraft=path.getText().toString();remote=next;changing=true;path.setText(remote?linkDraft:fileDraft);changing=false;resolvedTitle=resolvedFor=thumbnailPath=thumbnailFor="";thumbnail.setImageDrawable(null);}updateMode();commitPreview();}
            void updateTitle(){String value=path.getText().toString().trim();String caption=remote?(resolvedFor.equals(value)&&!resolvedTitle.isEmpty()?resolvedTitle:loading?"제목 확인 중…":"외부 링크"):!localTitle.isEmpty()&&localTitleFor.equals(value)?localTitle:fileName(value);title.setText("파일 "+number+(value.isEmpty()?"":" · "+caption));}
            void cancelPreview(){handler.removeCallbacks(previewDelayed);if(previewToken!=null)previewToken.cancel();previewToken=null;loading=false;}
            void commitPreview(){handler.removeCallbacks(previewDelayed);preview();}
            void preview(){
                if(!p.alive())return;
                String value=path.getText().toString().trim();boolean externalMode=remote;
                if(value.isEmpty()||externalMode&&(!ExternalMedia.isLink(value)||!AppStore.load(MainActivity.this).externalLinksConfirmed)||!externalMode&&!localAvailable(value))return;
                if(value.equals(thumbnailFor)&&(!externalMode||!resolvedTitle.isEmpty()))return;
                cancelPreview();ExternalMedia.CancellationToken token=new ExternalMedia.CancellationToken();previewToken=token;p.cancelTasks.add(token);loading=externalMode;updateTitle();
                mediaWork.execute(()->{try{ExternalMedia.Preview result=externalMode?ExternalMedia.preview(MainActivity.this,value,token):ExternalMedia.localPreview(MainActivity.this,value,token);handler.post(()->{if(p.alive()&&previewToken==token&&remote==externalMode&&path.getText().toString().trim().equals(value)){applyMetadata(value,result.title,result.thumbnailPath);loading=false;updateTitle();}});}
                    catch(Exception ignored){handler.post(()->{if(p.alive()&&previewToken==token){loading=false;updateTitle();}});}
                });
            }
            void applyMetadata(String uri,String caption,String thumbnailFile){if(!path.getText().toString().trim().equals(uri))return;if(caption!=null&&!caption.isEmpty()){if(remote){resolvedFor=uri;resolvedTitle=caption;}else{localTitleFor=uri;localTitle=caption;}}thumbnailFor=uri;thumbnailPath=thumbnailFile==null?"":thumbnailFile;showThumbnail(thumbnailPath);updateTitle();}
            void showThumbnail(String file){if(file==null||file.isEmpty()){thumbnail.setImageDrawable(null);return;}mediaWork.execute(()->{BitmapFactory.Options options=new BitmapFactory.Options();options.inSampleSize=2;Bitmap bitmap=BitmapFactory.decodeFile(file,options);handler.post(()->{if(p.alive()&&thumbnailPath.equals(file))thumbnail.setImageBitmap(bitmap);});});}
            Track read(){commitPreview();String value=path.getText().toString().trim();Track track=new Track(value,remote?(resolvedFor.equals(value)?resolvedTitle:""):(localTitleFor.equals(value)?localTitle:fileName(value)),remote);track.thumbnailPath=thumbnailFor.equals(value)?thumbnailPath:"";return track;}
        }
    }
    private final class Popup {
        final LinearLayout content=ui.column(),footer=ui.row(),body=ui.column();
        final TextView error=errorText();
        final Button save=ui.button("저장","save",true,false),cancel=ui.button("취소",null,false,false);
        final ScrollView scroll;
        final AlertDialog dialog;
        final List<ExternalMedia.CancellationToken> cancelTasks=new ArrayList<>();
        java.util.function.BooleanSupplier beforeCancel=()->true,beforeOutside=()->true;
        EditText commitField;
        Runnable afterDismiss=()->{};
        Popup(String category,String title,String caption){
            content.setPadding(dp(20),dp(20),dp(20),dp(4));
            if(!category.isEmpty()){TextView categoryView=ui.text(category,11,DesktopTheme.ACCENT);categoryView.setTypeface(ui.bold);content.addView(categoryView);}
            TextView titleView=ui.text(title,23,DesktopTheme.FOREGROUND);titleView.setTypeface(ui.bold);titleView.setPadding(0,dp(4),0,dp(8));content.addView(titleView);
            if(!caption.isEmpty()){TextView captionView=description(caption);captionView.setPadding(0,0,0,dp(14));content.addView(captionView);}
            error.setPadding(0,dp(10),0,dp(4));content.addView(error);
            scroll=new ScrollView(MainActivity.this){@Override protected void onMeasure(int width,int height){int maximum=(int)(getResources().getDisplayMetrics().heightPixels*.80)-dp(62);super.onMeasure(width,MeasureSpec.makeMeasureSpec(Math.max(dp(120),maximum),MeasureSpec.AT_MOST));}};
            scroll.addView(content);body.addView(scroll);
            footer.setGravity(Gravity.RIGHT|Gravity.CENTER_VERTICAL);footer.setPadding(dp(20),dp(12),dp(20),dp(18));footer.addView(cancel);space(footer,10);footer.addView(save);body.addView(footer);
            body.setBackgroundColor(DesktopTheme.BACKGROUND);dialog=new AlertDialog(MainActivity.this){
                @Override public void cancel(){try{if(beforeCancel.getAsBoolean())super.cancel();}catch(RuntimeException failure){error.setText(message(failure));}}
                @Override public boolean dispatchTouchEvent(MotionEvent event){if(event.getAction()==MotionEvent.ACTION_DOWN&&commitField!=null&&commitField.hasFocus()){Rect bounds=new Rect();commitField.getGlobalVisibleRect(bounds);if(!bounds.contains((int)event.getRawX(),(int)event.getRawY())){if(!beforeOutside.getAsBoolean())return true;commitField.clearFocus();}}return super.dispatchTouchEvent(event);}
            };dialog.setView(body);
            cancel.setOnClickListener(v->dialog.cancel());
            dialog.setOnDismissListener(d->{for(ExternalMedia.CancellationToken token:cancelTasks)token.cancel();if(pickerOwner==this){pickerOwner=null;pickerCallback=null;}openPopups.remove(this);afterDismiss.run();});
        }
        void add(View view){add(view,ui.fill());}
        void add(View view,LinearLayout.LayoutParams params){params.topMargin=dp(8);content.addView(view,content.getChildCount()-1,params);}
        boolean alive(){return dialog.isShowing()&&!isFinishing()&&!isDestroyed();}
        void show(){dialog.show();openPopups.add(this);dialog.getWindow().setBackgroundDrawable(ui.shape(DesktopTheme.BACKGROUND,12));dialog.getWindow().setSoftInputMode(android.view.WindowManager.LayoutParams.SOFT_INPUT_ADJUST_RESIZE);dialog.getWindow().setLayout(Math.min(getResources().getDisplayMetrics().widthPixels-dp(20),dp(720)),ViewGroup.LayoutParams.WRAP_CONTENT);}
        void dismiss(){dialog.dismiss();}
        void perform(Runnable operation){try{operation.run();}catch(RuntimeException failure){error.setTextColor(DesktopTheme.ERROR);error.setText(message(failure));}}
    }

    private void showFileLocation(String url,Popup parent){
        parent.perform(()->{
            ExternalMedia.Prepared prepared=ExternalMedia.getPrepared(this,url);
            if(prepared==null){parent.error.setText("다운로드된 파일이 없습니다. 링크를 저장해 먼저 오디오를 다운로드하세요.");return;}
            Popup location=new Popup("","다운로드된 파일 위치",ExternalMedia.fileLocation(this,url));
            TextView path=description(prepared.path);path.setTextIsSelectable(true);location.add(path);
            location.save.setText("파일로 저장");ui.style(location.save,"folder",true,false,false);location.cancel.setText("닫기");
            location.save.setOnClickListener(v->location.perform(()->{
                exportPath=prepared.path;Intent intent=new Intent(Intent.ACTION_CREATE_DOCUMENT);intent.addCategory(Intent.CATEGORY_OPENABLE);intent.setType("application/octet-stream");intent.putExtra(Intent.EXTRA_TITLE,new File(prepared.path).getName());startActivityForResult(intent,EXPORT_FILE);
            }));location.show();
        });
    }

    private void pickFiles(Popup owner,boolean multiple,Consumer<List<Track>> accepted){
        Intent intent=new Intent(Intent.ACTION_OPEN_DOCUMENT);intent.addCategory(Intent.CATEGORY_OPENABLE);intent.setType("*/*");intent.putExtra(Intent.EXTRA_ALLOW_MULTIPLE,multiple);intent.addFlags(Intent.FLAG_GRANT_READ_URI_PERMISSION|Intent.FLAG_GRANT_PERSISTABLE_URI_PERMISSION);
        pickerOwner=owner;pickerCallback=accepted;owner.perform(()->startActivityForResult(intent,PICK_FILE));
    }
    @Override protected void onActivityResult(int request,int result,Intent data){
        super.onActivityResult(request,result,data);
        if(request==EXPORT_FILE){
            if(result==RESULT_OK&&data!=null&&data.getData()!=null&&!exportPath.isEmpty()){
                Uri destination=data.getData();String source=exportPath;exportPath="";
                mediaWork.execute(()->{try(InputStream input=new FileInputStream(source);OutputStream output=getContentResolver().openOutputStream(destination,"w")){if(output==null)throw new java.io.IOException("파일을 저장할 위치를 열 수 없습니다.");byte[] buffer=new byte[81920];int count;while((count=input.read(buffer))!=-1)output.write(buffer,0,count);handler.post(()->toast("파일을 저장했습니다."));}catch(Exception failure){handler.post(()->toast(message(failure)));}});
            }return;
        }
        if(request!=PICK_FILE)return;Consumer<List<Track>> callback=pickerCallback;Popup owner=pickerOwner;pickerCallback=null;pickerOwner=null;
        if(result!=RESULT_OK||data==null||callback==null||owner==null||!owner.alive())return;
        owner.perform(()->{
            List<Uri> uris=new ArrayList<>();if(data.getClipData()!=null)for(int i=0;i<data.getClipData().getItemCount();i++)uris.add(data.getClipData().getItemAt(i).getUri());else if(data.getData()!=null)uris.add(data.getData());
            List<Track> tracks=new ArrayList<>();
            for(Uri uri:uris){
                if(uri==null||!"content".equals(uri.getScheme()))continue;
                if((data.getFlags()&Intent.FLAG_GRANT_READ_URI_PERMISSION)!=0){try{getContentResolver().takePersistableUriPermission(uri,Intent.FLAG_GRANT_READ_URI_PERMISSION);}catch(SecurityException ignored){owner.error.setText("이 파일 제공자는 파일 접근 권한을 계속 유지하지 않을 수 있습니다.");}}
                String name=uri.getLastPathSegment();try(Cursor cursor=getContentResolver().query(uri,new String[]{OpenableColumns.DISPLAY_NAME},null,null,null)){if(cursor!=null&&cursor.moveToFirst())name=cursor.getString(0);}catch(RuntimeException ignored){}
                tracks.add(new Track(uri.toString(),name==null?"":name,false));
            }callback.accept(tracks);
        });
    }

    private boolean localAvailable(String path){
        if(path==null||path.trim().isEmpty())return false;
        try{Uri uri=Uri.parse(path);if("content".equals(uri.getScheme())){try(android.content.res.AssetFileDescriptor file=getContentResolver().openAssetFileDescriptor(uri,"r")){return file!=null;}}return new File("file".equals(uri.getScheme())?uri.getPath():path).isFile();}
        catch(Exception unavailable){return false;}
    }
    private void showText(String title,String value){Popup p=new Popup("",title,"");TextView body=ui.text(value,12,DesktopTheme.FOREGROUND);body.setTextIsSelectable(true);p.add(body);p.save.setVisibility(View.GONE);p.cancel.setText("닫기");p.show();}
    private String licenseText(){
        StringBuilder text=new StringBuilder();appendLicenses(text,"licenses");return text.toString();
    }
    private void appendLicenses(StringBuilder text,String directory){try{String[] names=getAssets().list(directory);if(names==null)return;java.util.Arrays.sort(names);for(String name:names){String path=directory+"/"+name;if(name.equals("ExternalLinkUsage.ko.txt"))continue;String[] children=getAssets().list(path);if(children!=null&&children.length>0)appendLicenses(text,path);else{String contents=assetOptional(path);if(!contents.isEmpty()){if(text.length()>0)text.append("\n\n");text.append(contents);}}}}catch(java.io.IOException ignored){}}
    private String asset(String path){String text=assetOptional(path);return text.isEmpty()?"라이선스 및 이용 안내 문서를 열 수 없습니다. 저장소에 포함된 안내 문서를 확인하세요.":text;}
    private String assetOptional(String path){try(InputStream input=getAssets().open(path);ByteArrayOutputStream output=new ByteArrayOutputStream()){byte[] bytes=new byte[8192];int count;while((count=input.read(bytes))!=-1)output.write(bytes,0,count);return output.toString("UTF-8");}catch(Exception missing){return "";}}
    private LinearLayout section(String title){LinearLayout section=ui.column();section.setPadding(dp(12),dp(10),dp(12),dp(12));android.graphics.drawable.GradientDrawable background=ui.shape(DesktopTheme.CARD,12);background.setStroke(dp(1),DesktopTheme.BORDER);section.setBackground(background);if(title!=null){TextView label=ui.text(title,14,DesktopTheme.FOREGROUND);label.setTypeface(ui.bold);label.setPadding(0,0,0,dp(6));section.addView(label);}return section;}
    private TextView description(String text){TextView view=ui.text(text,12,DesktopTheme.MUTED);view.setPadding(0,dp(5),0,dp(5));return view;}
    private TextView errorText(){return ui.text("",12,DesktopTheme.ERROR);}
    private TextView link(String value){TextView view=ui.text(value,12,DesktopTheme.MUTED);view.setPaintFlags(view.getPaintFlags()|android.graphics.Paint.UNDERLINE_TEXT_FLAG);view.setMinHeight(dp(34));view.setGravity(Gravity.CENTER_VERTICAL);return view;}
    private static List<Track> copyTracks(List<Track> original){List<Track> copied=new ArrayList<>();for(Track track:original)copied.add(track.copy());return copied;}
    private static TextWatcher watcher(Runnable changed){return new TextWatcher(){public void beforeTextChanged(CharSequence text,int start,int count,int after){}public void onTextChanged(CharSequence text,int start,int before,int count){}public void afterTextChanged(Editable editable){changed.run();}};}
    private int boundedInput(EditText input,int maximum,int fallback){String text=input.getText().toString().trim();if(text.isEmpty())return fallback;try{return(int)Math.max(0,Math.min(maximum,Long.parseLong(text)));}catch(NumberFormatException invalid){return text.matches("[0-9]+")?maximum:fallback;}}
    private void keyboard(View field){((InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).showSoftInput(field,InputMethodManager.SHOW_IMPLICIT);}
    private void hideKeyboard(View field){((InputMethodManager)getSystemService(INPUT_METHOD_SERVICE)).hideSoftInputFromWindow(field.getWindowToken(),0);}
    private static void enableTree(View view,boolean enabled){view.setEnabled(enabled);if(view instanceof ViewGroup){ViewGroup group=(ViewGroup)view;for(int i=0;i<group.getChildCount();i++)enableTree(group.getChildAt(i),enabled);}}
    private void space(LinearLayout row,int width){row.addView(new View(this),new LinearLayout.LayoutParams(dp(width),1));}
    private int dp(float value){return ui.dp(value);}
    private static String fileName(String path){if(path==null||path.isEmpty())return "";try{String segment=Uri.parse(path).getLastPathSegment();return segment==null?path:segment;}catch(RuntimeException ignored){return path;}}
    private static String format(int seconds){return String.format(Locale.ROOT,"%02d:%02d:%02d",Math.max(0,seconds)/3600,Math.max(0,seconds)/60%60,Math.max(0,seconds)%60);}
    private static String message(Throwable failure){return failure.getMessage()==null?"동작을 완료하지 못했습니다. 다시 시도하세요.":failure.getMessage();}
    private void perform(Runnable operation){try{operation.run();}catch(RuntimeException failure){toast(message(failure));}}
    private void toast(String text){Toast.makeText(this,text,Toast.LENGTH_LONG).show();}
}
